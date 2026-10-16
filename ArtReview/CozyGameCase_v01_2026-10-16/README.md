# Cozy Game Case v01

Tek model: hafif pahlı köşeler, gömülü kapak alanı, ince birleşim çizgisi ve molded arka işaret. Kapalı mesh; gereksiz iç yüzey, ayrı latch/hinge renderer'ı veya subdivision yok.

- Düzenlenebilir kaynak: `CozyGameCase_v01.blend`; aktif sahnede yalnızca case var. Texture'lar blend içine paketli.
- Unity dosyaları: `Assets/GameAssets/Model/CozyGameCase_v01/`.
- `CozyGameCase_v01.fbx`: tek mesh, Shell=0 / Cover=1, Read/Write açık.
- `CozyGameCase_Source.prefab`: mevcut **Tools → Optimization → Create Combined Case Mesh** komutuyla kullanılabilecek kaynak; önizleme artwork'ü içerir.
- `CozyGameCase_Combined.asset`: mevcut CaseMeshCombiner koduyla üretilmiş hazır tek-submesh çıktı.
- `CozyGameCase_Visual.prefab`: combined mesh/material içeren hazır visual. Network/gameplay component'i içermez.

Mevcut GameBoxVisual'ın **scale (40,24,40), rotation Z180, merkez pivot** düzeni korunur. Ham mesh `.012 × .002 × .016`; görünür boyut `.48 × .048 × .64 m`. Mesh'i mevcut GameBoxVisual'a takarken transform'u tekrar ölçekleme. Yeni combined material'i de kullan: `CozyGameCase_Combined.mat` ortak `CozyGameCase_ShellTones.png` texture'ına bağlıdır.

Kaynakta 72 geometrik vertex; Unity UV/normal ayrımları sonrasında **272 vertex / 140 triangle**. Runtime visual **1 MeshRenderer, 1 submesh, 1 shared material**, 16-bit index kullanır. 4.000 görünür case için geometri toplamı 560.000 triangle olur; bu bir FPS veya draw-call garantisi değildir. Mevcut shader'ın batching/instancing davranışı değiştirilmedi.

Runtime kapak artwork'ü mevcut global `_GameCaseCoverArray` üzerinden gelir; kaynak prefab'deki SIDE QUEST görseli yalnızca önizleme içindir. Birleşim ve bevel tonları 256² ortak grayscale shell atlas'ından gelir; `_ShellColor` üzerinden playtime renkleri çalışmaya devam eder. UV1.x shell=0 / cover=1 maskesine ayrılmıştır; combined mesh'e lightmap UV üretimi veya UV swap uygulama.

Unity içinde mevcut combiner'ın gerçek metodu çalıştırıldı; submesh sırası, kapak normali/UV yönü, görünür boyutlar, 16-bit index ve kaydedilmiş UV1 maskesi doğrulandı. Blender FBX roundtrip ve Unity asset önizlemesi kontrol edildi. Sonuç: `unity-result.txt`.

Gameplay sahnesi ve NetworkGameCase prefab'i bu model için değiştirilmedi. Play Mode performansı, elde tutma ve multiplayer davranışı test edilmedi. Üretim script'i `build_game_case.py`; geçici Unity authoring aracı `Assets/Source/Editor/CozyGameCaseAssetBuilder.cs`.
