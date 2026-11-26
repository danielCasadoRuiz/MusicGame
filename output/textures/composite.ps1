Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition (Get-Content -LiteralPath 'C:\Users\VISYON\Documents\MusicGame\output\textures\scalp_composite.cs' -Raw) -ReferencedAssemblies System.Drawing
[ScalpEdit]::Run('C:\Users\VISYON\Documents\AvatarLab\AvatarLab\Assets\AvatarLab\Textures\Avatar\skinTest_variation_v5.png','C:\Users\VISYON\.codex\generated_images\01a1fb88-0c7e-7002-a370-64efa31bd616\exec-d8c6ed72-bc51-46fc-8444-88a65fa5648b.png','C:\Users\VISYON\Documents\MusicGame\output\textures\skinTest_variation_v7_rapat.png')
