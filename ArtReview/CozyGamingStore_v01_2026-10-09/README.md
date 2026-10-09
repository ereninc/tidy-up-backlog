# SIDE QUEST — Cozy Gaming Store v01

Düzenlenebilir environment art çalışması; Blender 5.2.2 LTS ile ayrı dosyada üretildi. Açık Blender oturumuna bağlanılmadı. Unity kodu, sahnesi, prefab'leri ve gameplay referansları değiştirilmedi.

## Dosyalar ve açılış

- [CozyGamingStore_v01.blend](CozyGamingStore_v01.blend): ana kaynak. Aktif sahne `CozyGamingStore_Review_v01`.
- [Environment FBX](CozyGamingStore_Environment.fbx): mimari, kasa, kapılar, dekorlar; raflar hariç.
- [Shelf placeholders FBX](CozyGamingStore_ShelfPlaceholders.fbx): aynı koordinat sisteminde ayrı raf yerleşimi.
- [Üretim script'i](build_cozy_store.py): parametreler dosyanın başında.
- [FBX yeniden import kontrolü](FBX_RoundTrip_Check.blend): iki export ayrı kontrol sahnelerinde.

Orijinal üç raf ve GameBox, kendi kaynak sahnelerinde korunur. Ana sahne bunların ortak mesh/material verilerini kullanır. Kullanıcı onayıyla eklenen 1.50 m yüksekliğinde iki sıralı türev `CozyStore_LowShelf_Source` sahnesinde düzenlenebilir parçalardan oluşur; orijinal raflar ölçeklenmedi.

## Yerleşim ve ölçek

Ana iç alan **12 × 18 m**, yükseklik **4.55 m**; arka depo **4 × 5 m**, yükseklik **3.40 m**. Uzunluk ve tavan yüksekliği mevcut 3.04–3.71 m raflara göre ayarlandı. Blender +Z yukarı; ön cephe -Y tarafında. Sahne metre ölçeğinde, export root scale `(1,1,1)`.

10 ayrı isimli raf root'u: dört duvar rafı, iki adayı oluşturan dört alçak raf yüzü, arkada orijinal çift taraflı raf ve girişte özel poster display. Adalarda iki alçak raf sırt sırta durur. Raflar environment ile birleştirilmedi; root adlarından gameplay prefab'leriyle değiştirilebilir. Özel display'in poster süsleri kendi root'u altında ayrı child objelerdir.

| Ölçülen net açıklık | Metre |
| --- | ---: |
| Sol / sağ ana koridor | 2.292 |
| Orta koridor | 2.659 |
| Ada ile arka raf arası | 2.149 |
| Arka rafın arkası | 2.363 |
| Arka raf uçları | 2.399 |
| Giriş, açık kanatlar ve kollar dahil | 2.045 |
| Depo kapısı, açık kanat dahil | 2.200 |
| Kasa ön yaklaşımı / personel arkası | 3.386 / 2.000 |
| Özel display ön yaklaşımı | 2.192 |

Önde 3.2 × 3.2 m (**10.24 m²**) boş organizing alanı bırakıldı. Oyun kutusu yığınları üretilmedi. Dolaşım arkadan da bağlanır; rafların iki kullanılabilir yüzüne erişilebilir. Depo kapısı satış alanı tarafındaki duvara 180° park edilerek içerdeki çalışma alanı açık tutuldu.

Ölçü kontrolü: insan 1.80 m; GameBox **0.48 × 0.048 × 0.64 m** (genişlik × kalınlık × yükseklik). Unity'deki `(40,24,40)` görsel scale tekrar uygulanmadı. Alçak raf yüzeyleri 0.13 ve 0.94 m; üst kutunun tepesi 1.58 m, önizleme göz yüksekliği 1.68 m. Referanslar export dışında ve normal sahnede gizli.

## Collection düzeni

- `CST01_Architecture`: duvarlar, zemin, depo ve ayrı mimari parçalar.
- `CST01_FrontWall_and_Entrance`: ön cepheyi düzenlerken gizle.
- `CST01_Ceiling_Hide_For_Editing`: tavanı düzenlerken gizle. Lambalar için ayrıca `CST01_CeilingFixtures`.
- `CST01_ShelfPlaceholders`: ayrı raf root'ları ve linked visual'lar.
- `CST01_Props`: kasa, özgün posterler, tabelalar, üç bitki ve depo objeleri.
- `CST01_PreviewLighting`: yalnızca önizleme kameraları/ışıkları.
- `CST01_ScaleReferences_NoExport`: gizli insan ve kutu referansı.
- `CST01_Export_Hidden`: transform'ları hazırlanmış, normalde gizli FBX kopyaları.

Kapı kanatları ayrı menteşe root'larından döner. Zemin karoları tek birleşik mesh'tir; modüler tekrarlar ortak mesh/material kullanır. Yazılar kaynakta düzenlenebilir, FBX'te mesh'tir. Trebuchet MS Bold font verisi blend içine paketlendi.

## Önizlemeler

Cycles / OptiX, 64 sample; AgX, exposure -2.0. Işıklar ve color management Unity'ye taşınmaz. İlk beş görüntü istenen temel açılardır:

1. [Girişten göz hizası](01_entrance_eye.png)
2. [Ana koridordan göz hizası](02_main_aisle_eye.png)
3. [Kasadan mağazaya](03_checkout_eye.png)
4. [Depo bağlantısı](04_stock_connection_eye.png)
5. [Tavan ve yüksek geçiş başlıkları gizli genel plan](05_layout_top.png)
6. [Dış cephe](06_front_storefront.png)
7. [Depo çalışma alanı](07_stock_workroom.png)
8. [İnsan ve GameBox ölçeği](08_scale_reference.png)
9. [Girişte özel display](09_front_display_eye.png)

## Materyal paleti / URP başlangıç değerleri

Tüm renkler sRGB HEX; metallic **0**. Smoothness sütunu `1 - Blender roughness` başlangıç karşılığıdır; farklı aydınlatmada aynı görünümü garanti etmez. CGS01 materyalleri onaylı raf kütüphanesiyle ortaktır.

| Materyal | Base Color | Roughness | URP Smoothness |
| --- | --- | ---: | ---: |
| CGS01_Warm_Oak | #9B6849 | .72 | .28 |
| CGS01_Soft_Cream | #E9DFCC | .78 | .22 |
| CGS01_Dusty_Blue | #789CA8 | .65 | .35 |
| CGS01_Mustard | #D5AE54 | .67 | .33 |
| CGS01_Preview_Ink | #374650 | .82 | .18 |
| CST01_Warm_Plaster | #F1E8D7 | .85 | .15 |
| CST01_Light_Wood | #C5A17C | .74 | .26 |
| CST01_Floor_Cream | #DDD5C5 | .82 | .18 |
| CST01_Floor_BlueGrey | #CBD2CD | .82 | .18 |
| CST01_Soft_Teal | #67958B | .76 | .24 |
| CST01_Muted_Terracotta | #B77D68 | .82 | .18 |
| CST01_Delivery_Cardboard | #B69C78 | .88 | .12 |
| CST01_Sage_Leaves | #829B70 | .83 | .17 |
| CST01_Lamp_Warm_Diffuser | #FFF1D2 | .55 | .45 |
| CST01_Window_Glass | #D6E5DF | .14 | .86 |

Unity'de URP/Lit materyallerini bu renklerle yeniden oluşturup isimlerine göre eşleştir. Cam için Transparent yüzey ve alpha ayarı gerekir; Blender transmission .96 / IOR 1.45 doğrudan URP/Lit karşılığı değildir. Lamba difüzöründe Blender emission strength 2 kullanıldı; Unity emission ve sahne ışıkları ayrıca ayarlanmalı. [Resmi URP/Lit materyal referansı](https://docs.unity3d.com/6000.0/Documentation/Manual/urp/lit-shader.html).

Unity import için başlangıç: Scale Factor **1**, Convert Units açık, Normals **Import**; environment ve placeholder root'larını aynı konum/rotasyon ve scale 1 ile kullan. FBX yönleri forward **-Z**, up **Y**. GameBox görsel scale'ini bu dosyalara uygulama. Kapı menteşe hiyerarşisini korumak için import sonrasında child root'ları birleştirme. Baked aydınlatma kullanacaksan Lightmap UV üretimi ayrıca değerlendirilmelidir. [Unity 6 Model Import Settings](https://docs.unity3d.com/6000.0/Documentation/Manual/FBXImporter-Model.html).

## Yapılan kontroller ve sınırları

| Export | Gerçek triangle | Mesh obje | Ortak materyal |
| --- | ---: | ---: | ---: |
| Environment | 41,893 | 211 | 15 |
| Shelf placeholders | 27,351 | 15 | 4 |

İki dosya toplam **69,244 triangle**, paylaşımlar dahil **15 farklı materyal**. Sayılar modifier uygulanmış export geometrisinden ölçüldü.

Dokuz render görsel olarak incelendi. Pozlama, üstten kameranın kadrajı, ölçek kamerası, depo kapısı, karton/tezgâh birleşimi ve poster yüzey normalleri düzeltildi. Raf/dekor bounding box penetrasyonu 0; karton/tezgâh penetrasyonu 0. Mimari birleşimlerde kasıtlı temas/örtüşmeler var. Beş poster çizim yüzeyi kasıtlı açık mesh; ön yüzleri doğrulandı. Diğer export mesh'leri kapalı, dışa dönük ve dejenere yüz içermiyor. Negatif scale yok.

FBX'ler yeni, boş Blender kontrol sahnelerine yeniden import edildi: metre ölçüsü, yön, root scale, kapı pivot/hiyerarşisi, obje bazında materyal slotları ve triangle/materyal eşleşmeleri geçti. En büyük world bounds farkı **0.0000024 m**. Kamera, ışık ve ölçü referansları export'ta bulunmuyor. Exporter'ın linked mesh'lerde verdiği materyal-index uyarılarına karşı yeniden import edilen gerçek materyal atamaları ayrıca karşılaştırıldı.

0.20 m grid ve 0.60 m çaplı insan yaklaşımıyla tüm yürünebilir hücreler ve depo dahil dokuz hedef birbirine bağlı; göz hizası kameraları yürünebilir alanda. Bu geometrik kontrol, dört oyunculu Unity Play Mode testi değildir.

Raporlar: [yerleşim ve ölçüler](store_report.json), [kaynak koruma kontrolü](source_validation_report.json), [FBX roundtrip](fbx_roundtrip_report.json). Kontrol script'leri: [kaynak](validate_store_source.py), [export](validate_store_exports.py).

**Unity import, URP görünümü, collider/kapı davranışı, FPS erişimi, dört oyunculu dolaşım, runtime performansı ve gameplay prefab değişimi Unity'de test edilmedi.** Bunlar projeye bağlama aşamasında kontrol edilmeli.

## Tekrar üretim

Bu klasörü çalışma dizini yapıp Blender executable yolunu yerel kurulumuna göre değiştir:

```powershell
& 'D:\SteamLibrary\steamapps\common\Blender\blender.exe' --background '.\CozyGamingStore_v01.blend' --python '.\build_cozy_store.py'
```

Render almadan üretmek için sona `-- --skip-renders`; seçili render'lar için `-- --render=01_entrance_eye,05_layout_top` ekle. Script yalnızca `cozy_store_owner = codex.cozy_gaming_store.v01` sahiplik işaretli objeleri yeniden üretir. Kullanıcı objelerini/orijinal rafları silmez. Kendi ürettiği parçalara yaptığın elle düzenlemeler yenilenir; revizyona başlamadan blend'in ayrı kopyasını kaydet. Raf yerleşim koordinatları script'te açıkça tanımlıdır; oda ölçüsü değişince geçiş kontrolleri yeniden çalışır.

## Yerleşim referansları

[Sort Them Ducks resmi mağaza sayfası](https://store.steampowered.com/app/4992070/Sort_Them_Ducks/) ve [Sort The Capybaras resmi mağaza sayfası](https://store.steampowered.com/app/5130480/Sort_The_Capybaras/) ekran görüntüleri dolaşım/dekor yoğunluğu için incelendi. İndirilen `reference_*_layout.jpg` dosyaları yalnızca referanstır, modele/export'a dahil değildir. SIDE QUEST tabelaları ve geometrik posterler bu çalışma için özgün üretildi; referans oyunların maskotları, logoları veya dekor asset'leri kullanılmadı.
