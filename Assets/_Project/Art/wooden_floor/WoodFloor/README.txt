BlobShip - Wood floor
=====================
Textures (seamless, 2048 px = 2 x 2 m):
  T_WoodFloor_Albedo  colour, alpha = smoothness
  T_WoodFloor_Normal  normal map (OpenGL)
  T_WoodFloor_Mask    terrain mask map: R metallic, G AO, B height, A smoothness

Unity Terrain : Terrain > Paint Texture > Edit Terrain Layers > Add Layer > TL_WoodFloor (tile size 2 x 2 m).
Meshes / cubes: M_WoodFloor (URP Lit, smoothness from albedo alpha). Set Tiling so one texture covers 2 m of
                your mesh UVs (e.g. a 2 m tile with 0..1 UVs: Tiling 1, a 1 m cube: Tiling 0.5).
If the material shows pink, pick Universal Render Pipeline/Lit on it and drop the three textures in again.
