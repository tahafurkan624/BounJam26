using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using FMODUnity;
using UnityEngine;

namespace BounJam
{
    /// <summary>
    /// Records what FMOD plays to a WAV file. Unity Recorder only hears Unity's own audio, and FMOD plays past it
    /// straight to the speakers, so this taps FMOD's final mix instead: a small DSP at the head of the master channel
    /// group copies every block on its way out (after the volume settings, so it is what the player hears) into a
    /// buffer, and a thread of its own writes that to disk. The game sounds the same while it records.
    /// The FMOD Recorder window (Tools/BounJam/FMOD Recorder) drives it in the editor, on its own or along with Unity
    /// Recorder's takes.
    /// </summary>
    public static class FmodCapture
    {
        public enum Format { Pcm16, Float32 }

        // Seconds of sound the buffer holds between the mixer and the writer thread.
        const int BufferSeconds = 4;

        // Kept here so the delegates live as long as FMOD may call them.
        static readonly FMOD.DSP_CREATE_CALLBACK CreateCallback = OnCreate;
        static readonly FMOD.DSP_READ_CALLBACK ReadCallback = OnRead;
        // The mixer thread's copy of each block; only that thread touches it.
        static float[] block = new float[8192];
        static Take current;
        // The DSP's own pointer as its callbacks see it (not its handle), from OnCreate during createDSP.
        static IntPtr created;
        // What the mixer did since the take started, to tell why a take came out empty.
        static int anyReads, reads, lastChannels;

        public static bool IsRecording => current != null;
        public static string FilePath => current?.path;
        public static int SampleRate => current?.sampleRate ?? 0;
        public static int Channels => current?.channels ?? 0;
        /// <summary>Seconds of sound recorded so far.</summary>
        public static double Seconds => current?.Seconds ?? 0d;
        /// <summary>The loudest sample of the latest block, 0-1 (above 1 when it clips).</summary>
        public static float Peak => current?.peak ?? 0f;
        /// <summary>What went wrong with the last take, or null.</summary>
        public static string LastError { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Install()
        {
            // Statics survive entering play mode with domain reload off.
            Stop();
            Application.quitting -= OnQuitting;
            Application.quitting += OnQuitting;
        }

        static void OnQuitting() => Stop();

        /// <summary>Starts recording FMOD's mix to <paramref name="path"/>, stopping any take already running.</summary>
        public static void Start(string path, Format format)
        {
            if (!Application.isPlaying) throw new InvalidOperationException("FMOD sound can only be recorded in play mode.");
            Stop();
            LastError = null;

            FMOD.System core = RuntimeManager.CoreSystem;
            Check(core.getSoftwareFormat(out int sampleRate, out FMOD.SPEAKERMODE speakerMode, out int channels), "getSoftwareFormat");
            if (speakerMode != FMOD.SPEAKERMODE.RAW) Check(core.getSpeakerModeChannels(speakerMode, out channels), "getSpeakerModeChannels");
            Check(core.getMasterChannelGroup(out FMOD.ChannelGroup master), "getMasterChannelGroup");

            var description = new FMOD.DSP_DESCRIPTION
            {
                name = Name("BounJam Capture"),
                numinputbuffers = 1,
                numoutputbuffers = 1,
                create = CreateCallback,
                read = ReadCallback,
            };
            created = IntPtr.Zero;
            Check(core.createDSP(ref description, out FMOD.DSP dsp), "createDSP");
            // The master bus mixes in its own format (5.1 in a new FMOD Studio project) and only meets the speakers' at the
            // output; this has FMOD mix it down for the recorder already, so the file has what the speakers get.
            FMOD.RESULT mixdown = dsp.setChannelFormat(0, channels, speakerMode);
            if (mixdown != FMOD.RESULT.OK)
            {
                dsp.release();
                throw new InvalidOperationException($"FMOD setChannelFormat failed: {mixdown}");
            }
            anyReads = reads = lastChannels = 0;
            Take take;
            try { take = new Take(path, format, sampleRate, channels, dsp, created, master); }
            catch
            {
                dsp.release();
                throw;
            }

            Volatile.Write(ref current, take);
            FMOD.RESULT added = master.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.HEAD, dsp);
            if (added != FMOD.RESULT.OK)
            {
                Stop();
                throw new InvalidOperationException($"FMOD addDSP failed: {added}");
            }
            dsp.setActive(true);
        }

        /// <summary>Stops the take and finishes its file; returns the file's path, or null when nothing was recording.</summary>
        public static string Stop()
        {
            Take take = current;
            if (take == null) return null;
            Volatile.Write(ref current, null);
            // FMOD may already be shut down (the game is quitting); the file is finished either way.
            take.master.removeDSP(take.dsp);
            take.dsp.release();
            take.Finish();

            if (take.error != null) LastError = "Couldn't write the file: " + take.error;
            else if (take.Dropped > 0)
                LastError = $"{(double)take.Dropped / (take.sampleRate * take.channels):0.00} s of sound was lost (the disk fell behind).";
            else if (take.Seconds == 0d)
                LastError = anyReads == 0 ? "FMOD mixed no sound while recording (was the editor paused?)."
                          : reads == 0 ? "The recorder's DSP ran but didn't know itself; nothing was recorded."
                          : $"FMOD mixed {lastChannels} channels, not the {take.channels} it was set up for; nothing was recorded.";
            return take.path;
        }

        [AOT.MonoPInvokeCallback(typeof(FMOD.DSP_CREATE_CALLBACK))]
        static FMOD.RESULT OnCreate(ref FMOD.DSP_STATE state)
        {
            created = state.instance;
            return FMOD.RESULT.OK;
        }

        [AOT.MonoPInvokeCallback(typeof(FMOD.DSP_READ_CALLBACK))]
        static FMOD.RESULT OnRead(ref FMOD.DSP_STATE state, IntPtr inbuffer, IntPtr outbuffer, uint length, int inchannels,
                                 ref int outchannels)
        {
            int count = (int)length * inchannels;
            if (block.Length < count) block = new float[count];
            Marshal.Copy(inbuffer, block, 0, count);
            Marshal.Copy(block, 0, outbuffer, count);
            outchannels = inchannels;

            anyReads++;
            Take take = Volatile.Read(ref current);
            // The DSP of a take that just stopped may still mix a last block; that one only passes the sound on.
            if (take == null || state.instance != take.instance) return FMOD.RESULT.OK;
            reads++;
            lastChannels = inchannels;
            if (inchannels == take.channels) take.Push(block, count);
            return FMOD.RESULT.OK;
        }

        static void Check(FMOD.RESULT result, string call)
        {
            if (result != FMOD.RESULT.OK) throw new InvalidOperationException($"FMOD {call} failed: {result}");
        }

        static byte[] Name(string name)
        {
            var bytes = new byte[32];
            Encoding.ASCII.GetBytes(name, 0, Math.Min(name.Length, 31), bytes, 0);
            return bytes;
        }

        /// <summary>
        /// One recording: a ring buffer the mixer thread fills (<see cref="Push"/>) and a writer thread empties into the
        /// WAV file. Each side only moves its own counter, so neither waits for the other.
        /// </summary>
        sealed class Take
        {
            public readonly string path;
            public readonly int sampleRate, channels;
            public readonly FMOD.DSP dsp;
            public readonly IntPtr instance;
            public readonly FMOD.ChannelGroup master;
            public float peak;
            public string error;

            readonly Format format;
            readonly float[] ring;
            readonly FileStream file;
            readonly Thread writer;
            long pushed, taken, dropped;
            volatile bool finishing;

            public Take(string path, Format format, int sampleRate, int channels, FMOD.DSP dsp, IntPtr instance,
                        FMOD.ChannelGroup master)
            {
                this.path = path;
                this.format = format;
                this.sampleRate = sampleRate;
                this.channels = channels;
                this.dsp = dsp;
                this.instance = instance;
                this.master = master;
                ring = new float[sampleRate * channels * BufferSeconds];

                Directory.CreateDirectory(Path.GetDirectoryName(path));
                file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                WriteHeader(0);
                writer = new Thread(Write) { IsBackground = true, Name = "FMOD capture writer" };
                writer.Start();
            }

            public double Seconds => (double)Volatile.Read(ref pushed) / (sampleRate * channels);
            public long Dropped => Interlocked.Read(ref dropped);

            /// <summary>Mixer thread: queues a block for the writer, or drops it when the writer has fallen behind.</summary>
            public void Push(float[] samples, int count)
            {
                long end = pushed;
                if (count > ring.Length - (end - Volatile.Read(ref taken)))
                {
                    Interlocked.Add(ref dropped, count);
                    return;
                }
                int start = (int)(end % ring.Length);
                int first = Math.Min(count, ring.Length - start);
                Array.Copy(samples, 0, ring, start, first);
                Array.Copy(samples, first, ring, 0, count - first);
                Volatile.Write(ref pushed, end + count);

                float loudest = 0f;
                for (int i = 0; i < count; i++) loudest = Math.Max(loudest, Math.Abs(samples[i]));
                peak = loudest;
            }

            /// <summary>Lets the writer empty the buffer, then waits for it to finish the file.</summary>
            public void Finish()
            {
                finishing = true;
                writer.Join();
            }

            void Write()
            {
                // Up to a quarter of the ring at a time, which is as many bytes as the ring has samples at 32 bits.
                int chunk = ring.Length / 4;
                var bytes = new byte[chunk * 4];
                long dataBytes = 0;
                try
                {
                    while (true)
                    {
                        // Read before emptying, so the last pass takes everything pushed before the stop.
                        bool last = finishing;
                        long end = Volatile.Read(ref pushed);
                        long at = taken;
                        while (at < end)
                        {
                            int start = (int)(at % ring.Length);
                            int count = (int)Math.Min(Math.Min(end - at, ring.Length - start), chunk);
                            int size = Encode(start, count, bytes);
                            file.Write(bytes, 0, size);
                            dataBytes += size;
                            at += count;
                            Volatile.Write(ref taken, at);
                        }
                        if (last) break;
                        Thread.Sleep(20);
                    }
                    file.Seek(0, SeekOrigin.Begin);
                    WriteHeader(dataBytes);
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
                finally
                {
                    file.Dispose();
                }
            }

            int Encode(int start, int count, byte[] bytes)
            {
                if (format == Format.Float32)
                {
                    Buffer.BlockCopy(ring, start * 4, bytes, 0, count * 4);
                    return count * 4;
                }
                for (int i = 0; i < count; i++)
                {
                    var sample = (short)Mathf.RoundToInt(Mathf.Clamp(ring[start + i], -1f, 1f) * short.MaxValue);
                    bytes[2 * i] = (byte)sample;
                    bytes[2 * i + 1] = (byte)(sample >> 8);
                }
                return count * 2;
            }

            void WriteHeader(long dataBytes)
            {
                int bits = format == Format.Pcm16 ? 16 : 32;
                int frameBytes = channels * bits / 8;
                using (var header = new BinaryWriter(file, Encoding.ASCII, true))
                {
                    header.Write(Encoding.ASCII.GetBytes("RIFF"));
                    header.Write((uint)(36 + dataBytes));
                    header.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    header.Write(16);
                    header.Write((short)(format == Format.Pcm16 ? 1 : 3)); // PCM, or IEEE float
                    header.Write((short)channels);
                    header.Write(sampleRate);
                    header.Write(sampleRate * frameBytes);
                    header.Write((short)frameBytes);
                    header.Write((short)bits);
                    header.Write(Encoding.ASCII.GetBytes("data"));
                    header.Write((uint)dataBytes);
                }
            }
        }
    }
}
