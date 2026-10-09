# Cozy Gaming Shelf — ilk görsel değerlendirme

Blender 5.2.2 LTS ile üretildi. Canlı Blender oturumuna doğrudan erişim bulunmadığından son kaydedilen `C:\Users\erenc\Desktop\6.1sol.blend` ayrı bir background süreçte okundu. Canlı oturumdaki kaydedilmemiş değişiklikler bu çalışmaya dahil değildir. Orijinal dosya üzerine kayıt yapılmadı.

> Aynı `.blend` dosyasına çift taraflı raf ve posterli özel stand da eklendi. Yeni sahneler, render'lar ve kullanım bilgileri: [VARIANTS.md](VARIANTS.md). İlk model korunuyor.

## Teslim dosyaları

- `CozyGamingShelf_v01.blend`: Düzenlenebilir model, referanslar ve önizleme sahnesi.
- `build_gaming_shelf.py`: Tekrar çalıştırılabilir üretim script'i; boyut, satır sayısı, panel/raf kalınlığı ve bevel ayarları başında.
- `01_front.png`: Ön görünüş, 30 referans kutu.
- `02_three_quarter.png`: Üç çeyrek görünüş, 30 referans kutu.
- `03_fit_closeup.png`: Kutu aralıkları ve satır üst payını gösteren yakın görünüş.
- `04_empty_structure.png`: Kutusuz rafın yüzeyleri, yan panelleri ve arka paneli.
- `model_report.json`: Gerçek triangle sayısı, mesh sınırları ve 30 kutunun tek tek sığma ölçümleri.
- `validation_report.json`: Kaydedilmiş dosya, orijinal referansların korunması ve export kopyası kontrolleri.

Unity gameplay kodu, sahne/prefab referansları veya slot/row/NetworkObject yapısı değiştirilmedi. FBX export ya da Unity sahnesine yerleştirme yapılmadı.

## Ölçek kararı

Unity'deki `NetworkGameCase.prefab`, GameBox visual'ında `(40,24,40)` kullanıyor. Bu visual mevcut `GameBox_Combined.asset` mesh'ine bağlı:

| Ölçüm | X / genişlik | Y / kalınlık | Z / yükseklik |
|---|---:|---:|---:|
| Blender GameBox mesh'i | 1.2 | 0.2 | 1.6 |
| Blender orijinal object scale | 1 | 1 | 1 |
| Unity'deki kullanılan mesh | 0.012 | 0.002 | 0.016 |
| Unity visual localScale | 40 | 24 | 40 |
| Unity'deki görünür kutu | **0.48** | **0.048** | **0.64** |
| Blender önizleme kopyasının scale'i | **0.4** | **0.24** | **0.4** |

Unity prefab root scale'i ve GameplayScene içindeki 4000 instance'ın parent scale zincirleri `(1,1,1)`; instance root scale override'ı bulunmadı. Blender referansı Unity boyutuna önceden dönüştürülmüş değildir. `(40,24,40)` Blender mesh'ine doğrudan tekrar uygulanmadı.

Blender referansı Z-up, kapak yüzeyi -Y yönüne bakıyor. Önizleme kopyaları dik, kapakları öne bakacak şekilde aynı mesh'i paylaşıyor. Orijinal `GameBox` ve `Placeholder_Shelf_Type1` mesh'leri, materyalleri ve transform'ları, orijinal dosyadan yeniden okunarak karşılaştırıldı ve aynı kaldıkları doğrulandı.

## Model ölçüleri

- Gerçek mesh sınırı: **5.528 genişlik × 0.7875 derinlik × 3.040 yükseklik**.
- Bu ölçüler Unity world unit karşılığıdır; Blender sahnesi metric / scale length 1 kullanır.
- Üç kullanılabilir satır, her satırda öne bakan 10 kapak: toplam 30 kutu.
- Kutu arası net boşluk: **0.032**. Satırın sol/sağ uç payı: **0.07**.
- Raf üst yüzeyleri: **Z = 0.32, 1.18, 2.04**. Satır adımı: **0.86**.
- Kutu üstü net iç pay: **0.135**. Ön kenarlık ve isim plakasını aşarak dik yüklemede üst satırda en az **0.062**, diğer satırlarda **0.097** yükseklik payı kalır. Kutular tabla üstüne tam oturur.
- Gerçek export kopyası: **2620 triangle**, **4 ortak materyal**. Önizleme kutuları, kapak grafikleri, satır yazıları, zemin, kameralar ve ışıklar bu sayıya dahil değildir.
- Düzenlenebilir kaynakta 30 mesh/font parça; export hazırlığında bunların modifier uygulanmış tek mesh kopyası bulunur.
- Export kopyasında location/rotation sıfır, scale `(1,1,1)`; pivot zeminde, tabanın merkezinde. Negatif scale yok. Açık/non-manifold edge yok.

10 kutunun kapakları öne baktığından genişlik 5.5 world unit civarındadır. Bu genişlik Unity'de ölçülen 0.48 genişliğindeki kutulardan türetilmiştir; kutuları rafta sığdırmak için küçültme yapılmamıştır.

## Collection düzeni

`CozyGamingShelf_Review_v01` ayrı değerlendirme sahnesidir. Orijinal sahne ve kullanıcı objeleri korunur.

1. `CGS01_01_Editable_Shelf`: Sadece raf parçaları; canlı bevel modifier'ları ve düzenlenebilir header yazısı. `Module_Pivot_Ground_Center` parent'ı taban merkezindedir.
2. `CGS01_02_Preview_GameBoxes`: GameBox kopyaları, sade kapak grafikleri ve örnek satır yazıları. Orijinal materyaller değiştirilmeden, yalnız bu kopyalarda object-level materyal override'ları kullanılır.
3. `CGS01_03_Preview_Studio`: Önizleme zemini, üç kamera ve üç area light.
4. `CGS01_04_Export_Ready_Copy`: Görünümü kapalı, uygulanmış transform/modifier'lı tek mesh. Bu collection henüz dosyaya export edilmedi. Yalnız bunu export etmek referansları ve stüdyoyu dışarıda bırakır.

Satır isim plakaları export'ta boştur; `COZY PICKS / ARCADE / ADVENTURE` yazıları önizleme collection'ındadır. Üstteki `PLAY` ve küçük controller işareti rafın parçasıdır.

## Renk ve URP karşılıkları

Palet, verilen referansların sıcak ahşap/krem ana kütlelerinden ve soluk mavi/hardal vurgularından yorumlandı. Bunlar ekran görüntüsünden kesin piksel örnekleri değildir. Ahşap dokusu, bump/noise, subdivision veya yeni kapak texture'ı kullanılmadı.

| Raf materyali | sRGB Base Color | Roughness | Metallic | URP Smoothness başlangıcı |
|---|---|---:|---:|---:|
| Warm Oak | `#9B6849` | 0.72 | 0 | 0.28 |
| Soft Cream | `#E9DFCC` | 0.78 | 0 | 0.22 |
| Dusty Blue | `#789CA8` | 0.65 | 0 | 0.35 |
| Mustard | `#D5AE54` | 0.67 | 0 | 0.33 |

URP Lit'te bu renkleri elle temel al; Blender materyal görünümünün aynen taşınacağı varsayılmamalı. Roughness → Smoothness tersleme yalnız başlangıç değeridir; Unity ışığı ve color management altında son görsel ayar gerekir.

Önizleme Cycles CPU, 48 samples ve denoising kullanır. Standard/sRGB, gamma 1, exposure -1.5; yumuşak nötr dolgu ve az sıcak ana ışık vardır. İlk render'daki fazla pozlama düzeltilip tüm son render'lar yeniden incelendi.

## Tekrar üretme

Blender'da teslim edilen `.blend` dosyasını aç, Scripting/Text Editor'da `build_gaming_shelf.py` dosyasını açıp Run Script çalıştır. Alternatif:

```powershell
& 'D:\SteamLibrary\steamapps\common\Blender\blender.exe' --background '.\CozyGamingShelf_v01.blend' --python '.\build_gaming_shelf.py'
```

Render almadan yalnız modeli yenilemek için sona `-- --skip-renders` eklenebilir. Script sadece kendi `cozy_shelf_owner` etiketi olan objeleri yeniler; kullanıcı objelerini veya orijinal sahneyi silmez. Kendi teslim `.blend` ve render dosyalarını günceller. Başka bir klasöre yeni varyant üretmek için script'i o klasöre kopyalayabilir veya başındaki `OUTPUT_DIR` değerini değiştirebilirsin.

Script mevcut dosyada GameBox bulamazsa `REFERENCE_BLEND` içinden GameBox'ı yükler. Farklı makinede boş sahneden çalıştırırken bu yolu ayarla. Unity dosyaları üretim sırasında değiştirilmez.

## Kontroller

- Kaydedilmiş `.blend` yeniden açıldı; orijinal iki objenin geometry/material/transform karşılaştırması geçti.
- Script üretilmiş dosya üzerinde tekrar çalıştırıldı; duplicate üretilmiş objeler oluşmadı.
- 30 gerçek GameBox mesh kopyası için tabla temas yüksekliği, yan/üst/arka/ön kenarlık payları ve komşu kutu aralıkları kontrol edildi.
- Kaynak raf mesh'lerinde degenerate yüzey veya non-manifold edge bulunmadı; export font kopyasının seam vertex'leri ayrıca birleştirildi ve son mesh'te non-manifold edge sayısı sıfır.
- Ön, üç çeyrek, yakın ve boş raf render'ları görsel olarak incelendi. Fazla pozlama, ön kenarlık-tabla aralığı ve dekoratif D-pad üzerindeki üst üste yüzeyler düzeltildi. Üst header, kutular ön kenarlıktan dik geçerken de rahat pay bırakacak şekilde yükseltildi.
- Bu teslim görsel değerlendirme içindir. Unity import / FBX eksen-ölçek kontrolü ve mevcut gameplay slotlarına eşleme henüz yapılmadı.
