# Asset Recall 🔄  
*A lightweight Unity Editor tool to track and revisit recently selected assets and scene objects.*  

> **By [Compile & Co.](#)** — Simple tools for better workflows.

---

## ✨ Features
- ✅ Tracks both **project assets** and **hierarchy scene objects**
- 📌 **Pin** favorite items for quick access
- 🧠 Intelligent **filtering** by type (Prefab, Script, Scene, etc.)
- 💾 Saves selection **history to disk (JSON)**
- 🔁 Automatically removes missing or deleted assets
- 🚀 Lightweight & optimized with **.asmdef** support

---

## 🛠️ Setup

1. Place the `AssetRecall` folder anywhere inside your **Assets** directory  
2. Open the window from the Unity top menu:  
   **`Tools > Asset Recall`**

> ✅ No need to keep the folder inside `Assets/Editor/` — it works from anywhere!

---

## 🧱 Folder Structure

AssetRecall/
├── Editor/                        # All editor-related code and logic
│   ├── Data/                      # Serialized data types and containers
│   ├── GUI/                       # UI styles and icon definitions
│   ├── Resources/                 # Editor-only resources (if needed)
│   ├── Utils/                     # Helper utilities, constants, path resolvers
│   └── Window/                    # Editor window UI logic (main window class)
├── Resources/                     # Runtime-visible data (e.g., JSON history file)
└── README                         # Overview and documentation


---

## 📸 Preview

> _(Insert a short .gif or screenshot showing selection → pin → filter usage here. Optional but highly recommended!)_

---

## 🧪 Tested With

- ✅ Unity **2021.3 LTS**
- ✅ Unity **2022.3 LTS**
- ✅ Unity **2023.1+**
- 💡 Compatible with **URP**, **HDRP**, and **Built-in RP**
- 💡 Cross-platform: **Windows**, **macOS**, **Linux**

---

## 💡 Usage Tips

- Selection history is saved in `AssetRecall/Resources/AssetRecall_History.json`
- History is preserved across domain reloads and scene changes
- Deleted or missing objects are automatically removed from history
- Internally throttled via `EditorApplication.update` for smooth performance

---

## 🧩 Assembly Definition (Optional)

The tool supports `.asmdef` for clean modularization.

To enable:
1. Right-click on the `AssetRecall` folder
2. Select `Create > Assembly Definition`
3. Name it `AssetRecall`
4. Add reference to this asmdef from your main editor code, if necessary

---

## 🧃 Developed by

**exwitcher** — for [Compile & Co.](#)  
GitHub: [github.com/exwitcher](https://github.com/exwitcher)

---

## ❤️ Like this tool?

- ⭐ Star it on GitHub  
- 🐛 Found a bug? Open an issue  
- 🤝 Want to contribute? PRs are welcome!

---