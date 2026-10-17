# Counter PC

`Assets/Scene/03_GameplayScene.unity` içindeki bağımsız **CounterComputer** instance'ı mevcut counter monitörüne bağlandı. Orijinal model/mesh ve materyaller korunur; eski sabit ekran yazısının Renderer'ı canlı ekranın altında kalmaması için kapalıdır.

Prefab: `Assets/GameAssets/Prefabs/Computer/CounterComputer.prefab`.

- Root: `NetworkObject`, `NetworkStoreComputer`, interaction `BoxCollider`, `ComputerLocalPresenter`, `StoreComputerDesktopPresenter`.
- `WorldScreen`: 16:9 ekran mesh'i ve ortak URP Unlit materyal. Runtime RenderTexture, MaterialPropertyBlock üzerinden atanır.
- `ScreenViewAnchor`: yalnız yerel oyuncu kamerasının hedefi. Geçiş süresi/easing/FOV/near clip, Local Presenter Inspector'ındadır.
- `DesktopUI`: tek desktop/app Canvas'ı, `ComputerDesktopRaycaster`, `UIPanelFocus`. Gamepad default selection ve Back, mevcut scene coordinator üzerinden yönetilir.
- `DesktopRenderCamera`: yalnız UI layer'ını 1280×720 RT'ye çizer. Çözünürlük Desktop Presenter Inspector'ından ayarlanabilir.
- `ComputerFullscreen`: aynı RT'nin kullanım görüntüsü. Kullanımda oyuncunun kamerasına bağlanır; mevcut post-processing yolu korunur. Ayrı EventSystem yoktur.

Scene instance'ındaki focus coordinator ve StackInfoPanel CanvasGroup referansları hazırdır. Başka sahnede kullanılırsa mevcut `UIFocusCoordinator` atanabilir; presenter yerelde bulmayı da destekler. HUD grupları opsiyoneldir. PC `serverCooldown=0` kullanır; meşgul talepler normal interaction doğrulamasından sonra `Computer is not available.` mesajını alır.

Network state yalnız server'da değişir. Kullanım token'ı eski çıkış/app taleplerini reddeder. `UseStarted(ulong clientId, uint token)`, `UseEnded(ulong clientId, uint token)`, `AppChanged(StoreComputerApp)` ve `StateChanged(StoreComputerSnapshot)` event'leri genişletme noktalarıdır. App kapatma Desktop'a döner; Leave Computer açık app'i korur. Gerçek upgrade/satın alma logic'i eklenmedi.

Editor kurulum kontrolü: `Tools > Computer > Validate Counter PC`.

İzole host testi: `Tools > Computer > Run Isolated Host Validation`. Mevcut sahneyi kaydeder, geçici boş sahnede test eder ve önceki sahneyi/başlangıç tercihlerini geri yükler. `playmode-validation.txt`, gerçekten çalıştırılan kontrollerin raporudur; `inspection.txt` scene/prefab referans raporudur. Editor setup/validation araçları tek seferlik ve runtime dışında tutulmuştur.

İki ayrı oyun sürecinde host/client rekabeti, farklı RPC sender, remote disconnect ve transport late join ayrıca denenmelidir. Fiziksel gamepad/mouse, 16:9 ile 16:10 Game View'da kenar tıklamaları, gerçek FPS player/held-stack, pause/debug birlikte açıkken cursor davranışı ve görsel parlaklık/geçiş kontrolü de manuel kalır. Sanal input event testi ve matematik kontrolü bu görsel/donanım kontrollerinin yerine geçmez.

PC görüntüsünün arkasındaki dünya kenarlarının görünmesi için Sobel düzeltmesi de eklendi. `StoreComputerDesktopPresenter`, görüntünün ekran alanını ve görüntü/matte opaklığını yalnız ilgili kamera render'ında shader'a aktarır; diğer kameralar ve çıkış/disable sırasında maskeyi temizler. `EXW_SceneSobelOutline.shader`, UI tarafından örtülen piksellerde dünya derinliğinden outline üretmez. Canvas ve post-processing yolu değişmez. `sobel-validation.txt`, güncel Sobel pass'inin Unity shader compiler sonucudur; yeni Inspector bağlantısı gerekmez.
