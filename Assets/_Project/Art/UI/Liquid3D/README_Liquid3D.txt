BlobShip - 3D Sıvı Arayüz (turkuaz)
====================================

Ne var:
  Shaders/LiquidSlab.shader   kalın jöle/cam gövde (URP): arkasını kırarak gösterir, içten parlar, stüdyo yansıması, dalgalar
  Shaders/LiquidSim.shader    dalga simülasyonu (tıklama halkaları, hover izi)
  Scripts/LiquidSlab          herhangi bir UI nesnesini 3D sıvı butona / cam panele çevirir
  Scripts/LiquidToggle        Sıvı / Katı anahtarı (sıvı uçtan uca çalkalanır, Katı'da tüm butonlar donar)
  Scripts/LiquidPanel3D       cam panel: yukarıdan dökülerek açılır, aşağı akıp damlayarak kapanır
  Scripts/LiquidLEDLabel      TextMeshPro yazıyı beyaz LED gibi yapar (keskin çekirdek + küçük hale)
  Scripts/LiquidViewTilt      grubu hafif geriye yatırır: kamera alttan bakar, butonların alt kalınlığı görünür
  Editor/LiquidUIMenu         menüler (aşağıda)

Kurulum (bir kez):
  1. Zip'teki Assets klasörünü projenin içine kopyala (Assets/BlobShip/UI/Liquid3D).
  2. Tools > BlobShip > Liquid UI > Set Up URP
       URP asset: Opaque Texture + HDR açılır, Canvas kamerasında Post Processing açılır,
       sahneye "Liquid UI Bloom" (eşik 1: sadece LED yazı ve parlak yansımalar parlar) eklenir.
     Canvas "Screen Space - Camera" olmalı.

Hazır menü:
  Canvas'ı seç > sağ tık > BlobShip > Liquid 3D Pause Menu
  Cam panel + "Duraklatıldı" + Devam et / Yeniden başlat / Ana menü + altta Sıvı-Katı anahtarı kurulur.
  "Devam et" paneli akıtarak kapatır (Button.onClick'e bağlı). Diğerlerini kendi kodunla bağla.

Kendi butonlarını çevirmek:
  Butonları seç > Tools > BlobShip > Liquid UI > Make Selected Liquid
  Image görünmez olur ama tıklamayı almaya devam eder; Button.onClick aynen çalışır.

Koddan:
  LiquidUI.SetSolid(true);                         // Katı: tüm sıvı butonlar donar
  LiquidUI.SolidChanged += katı => ...;            // anahtara basılınca blob'u da değiştir
  panel.GetComponent<LiquidPanel3D>().Show();
  panel.GetComponent<LiquidPanel3D>().Hide(() => SceneManager.LoadScene(0));
  buton.GetComponent<LiquidSlab>().Poke();         // klavye / gamepad ile seçilince basma efekti

Renkler: LiquidSlab > Colors (Core / Rim / Absorb). Hazır palet: LiquidPalette.Turquoise / Cyan / Aqua.
Not: Overlay kamera kullanıyorsan Post Processing'i yığındaki son kamerada aç.
