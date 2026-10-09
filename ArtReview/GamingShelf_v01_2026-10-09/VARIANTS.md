# Çift taraflı raf ve özel poster standı

İki model **aynı `CozyGamingShelf_v01.blend` dosyasına** eklendi. Önceki tek taraflı raf, GameBox referansı, orijinal placeholder raf ve mevcut materyaller korunuyor. Ekleme öncesi dosya `CozyGamingShelf_v01_before_variants.blend` adıyla yedeklendi. Masaüstündeki kaynak dosyaya veya canlı Blender oturumuna yazılmadı.

## Blender'da bulma

Üst çubuktaki **Scene** seçicisinden:

- `CozyGamingShelf_Review_v01`: Önceden onaylanan ilk raf; değişmedi.
- `CozyGamingShelf_DoubleSided_v01`: Yeni çift taraflı raf; dosya bu sahneyle açılır.
- `CozyGamingShelf_Featured_v01`: Yeni posterli özel teşhir standı.

Yeni sahnelerin collection düzeni:

- `CGS02_01_Editable_Model` / `CGS03_01_Editable_Model`: Düzenlenebilir gerçek model parçaları. Bevel modifier'ları korunur.
- `02_Preview_Only`: GameBox kopyaları, kutu kapak grafikleri, örnek etiket yazıları ve poster illüstrasyonu. Modelden ayrıdır.
- `03_Studio_Only`: Kamera, ışık ve zemin.
- `04_ExportReady_Hidden`: Bevel/yazıları mesh'e dönüştürülmüş, transform'ları uygulanmış tek bir kopya. Başlangıçta viewport ve render'da gizli. **FBX export yapılmadı.**

Her modelin pivot'u tabanın zemin merkezinde `(0,0,0)`; Blender önü `-Y`, yukarısı `+Z`. Kaynak parçalar ilgili pivot Empty'sine bağlı. Negatif scale veya subdivision kullanılmadı.

## Modeller

| Model | Düzen | Gerçek dış boyut, genişlik × derinlik × yükseklik | Değerlendirilmiş triangle | Ortak materyal |
|---|---|---|---:|---:|
| Çift taraflı | Her yüzde 4 sıra × 10 kutu; toplam 80 önizleme kutusu | 5.528 × 1.474 × 3.710 | 4764 | 4 |
| Özel stand | 5 kanal × 3 derinlik; toplam 15 önizleme kutusu | 2.958 × 1.0985 × 3.275 | 3876 | 4 |

Triangle sayıları gizli model kopyalarından `calc_loop_triangles()` ile ölçüldü; önizleme kutuları, poster illüstrasyonu ve stüdyo bu sayılara dahil değil. Çift rafın 55, özel standın 38 düzenlenebilir mesh/font parçası var.

Çift taraflı rafta açık uçlar, ortak krem sırt, ahşap raf yüzeyleri, soluk mavi ön kenarlıklar ve iki yönde etiket plakaları bulunuyor. Özel standın üst kısmı hafif öne açılıyor; ortada çerçeveli poster alanı, altta düz kutu yüzeyi ve ince kanal ayırıcıları var.

GameBox ölçüsü ilk çalışmadaki doğrulanmış Unity boyutlarıyla aynı: **0.48 genişlik × 0.048 kalınlık × 0.64 yükseklik**. Blender referans mesh'inden kopya scale'i yaklaşık `(0.4,0.24,0.4)`; Unity'deki `(40,24,40)` tekrar uygulanmadı. Kopyalar orijinal mesh'i paylaşır; renk değişiklikleri sadece object-linked material override kullanır.

## Poster değiştirme

`CGS03_Poster_Replaceable_Surface` ayrı bir kaynak parçadır; görünen alanı **2.10 × 1.25**. Ön yüz `-Y` yönüne bakar. `PosterUV` katmanı ön yüzü 0–1 aralığına eşler. İleride Unity'de kendi poster materyalini bu parçaya uygulayabilirsin. Örnek dağ/ay illüstrasyonu ve yazıları `CGS03_02_Preview_Only` içindedir; hazır model kopyasına dahil edilmez.

## Palet

Yeni modeller ilk rafın **aynı materyal datablock'larını** kullanır; yeni mobilya materyali oluşturulmadı. Metallic hepsinde `0`.

| Materyal | sRGB renk | Blender roughness | URP için başlangıç smoothness |
|---|---|---:|---:|
| `CGS01_Warm_Oak` | `#9B6849` | 0.72 | 0.28 |
| `CGS01_Soft_Cream` | `#E9DFCC` | 0.78 | 0.22 |
| `CGS01_Dusty_Blue` | `#789CA8` | 0.65 | 0.35 |
| `CGS01_Mustard` | `#D5AE54` | 0.67 | 0.33 |

URP materyalleri otomatik kurulmadı; ışık ve tonemapping nedeniyle Blender görünümüyle ayrıca eşleştirilmesi gerekir.

## İncelenen render'lar

- `05_double_front.png`: Çift raf, ön üç çeyrek, 40 ön yüz kutusu.
- `06_double_back.png`: Arka üç çeyrek, diğer 40 kutu ve doğru yöne bakan kapaklar.
- `07_double_empty_end.png`: Kutusuz görünüş; iki taraftaki raf derinlikleri ve açık uçlar.
- `08_featured_three_quarter.png`: Özel standın tamamı, poster ve 15 kutu.
- `09_featured_front.png`: Önden poster, çerçeve ve beş kanal düzeni.
- `10_featured_box_fit.png`: Kutuların üç derinlik sırasını ve alt bölmeyi gösteren yakın görünüş.

Cycles CPU, 48 sample, denoise, 1440×1080; ilk modelle aynı world ve renk yönetimi kullanıldı. Render'lar görsel olarak incelendi. İlk geçişte fark edilen kapak yüzeyi çakışmaları ve kırpılan kadrajlar düzeltilip altı render yeniden alındı.

## Tekrar üretme

`build_shelf_variants.py` başında satır/adet, boyut, sıra yüksekliği, panel/raf kalınlığı ve bevel ayarları bulunur. Özel standın dekoratif profil/poster ayrıntıları ayrıca adlandırılmış kaynak parçalardan düzenlenebilir.

```powershell
& 'D:\SteamLibrary\steamapps\common\Blender\blender.exe' --background 'CozyGamingShelf_v01.blend' --python 'build_shelf_variants.py'
```

Komutu bu klasörde çalıştır. Sadece geometri için sonuna `-- --skip-renders` ekle. Tek bir render için örneğin `-- --render=08_featured_three_quarter` kullanabilirsin. Script yalnızca kendi `codex.cozy_gaming_shelf.variants.v01` etiketli nesnelerini yeniler; bu yeni modeller üzerinde elle yaptığın değişiklikleri tekrar üretim öncesi ayrı dosyaya kaydet. İlk modelin üretim script'i değiştirilmedi.

## Doğrulama

`validate_variants.py` kaydedilen dosyayı yeniden açarak şu kontrolleri yaptı; sonuçlar `variants_validation_report.json` ve ölçümler `variants_report.json` içinde:

- İlk rafın ve orijinal referansların mesh'leri, transform'ları, parent/collection bağlantıları, materyalleri ve ilk önizleme sahnesi yedekle aynı.
- 80 + 15 kutunun tabanı raf yüzeyine oturuyor; kutu–kutu ve kutu–model sınır kutularında hacimsel penetrasyon yok.
- Raf kapasitesi, yan boşluklar, ön/arka boşluklar ve baş mesafeleri pozitif.
- Mesh parçalarının dışa bakan normalleri ve pozitif hacimleri üretimde kontrol edildi.
- Hazır kopyalarda nonmanifold kenar ve dejenere yüz sayısı sıfır; export transform'ları identity, pivot zemin merkezinde.
- Script iki kez çalıştırıldı; `.001` gibi tekrarlı üretilmiş nesne yok.
- Poster ön yüz UV'si var; her yeni model ilk rafın dört ortak materyalini kullanıyor.

Unity gameplay kodu, sahne, prefab, slot/row ve NetworkObject referansları değiştirilmedi. Unity import, URP materyal eşleştirme, oyun içi ölçek/görünüm ve FBX kontrolleri **yapılmadı**; bu teslim görsel inceleme içindir.
