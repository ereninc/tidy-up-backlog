# Color Universe 🎨  
*A minimal yet powerful Unity Editor tool to create and manage multi-palette color grids.*  

> **By [Compile & Co.](#)** — Simple tools for better workflows.

---

## ✨ Features
- 🎨 Supports multiple named **color palettes**
- 📋 Each palette contains a **customizable grid** (5xN) of colors
- 🖱️ Left-click to **copy color hex**, right-click to **open color picker**
- 🧠 Smart persistence with **auto-save** and **restore**
- 📦 Colors saved as JSON in `Resources`, organized by palette
- 🧹 Clear, randomize, edit, or delete palettes with one click
- 🚀 Clean, lightweight, and modular with `.asmdef` support

---

## 🛠️ Setup

1. Import package from Asset Store  
2. Open the window from Unity’s top menu:  
   **`Tools > CompileCo > Color Universe Window`**

> ✅ No need to place it inside `Assets/Editor/` — it works from anywhere!

---

## 🧱 Folder Structure

ColorUniverse/  
├── Editor/                        # All editor logic  
│   ├── Data/                      # ScriptableObjects and palette data  
│   ├── GUI/                       # Icons, popup windows, styles  
│   ├── Utils/                     # Helper methods, color logic  
│   └── Window/                    # Main editor window class  
├── Resources/                     # Saved palette data (as JSON)  
└── README.md                      # You are here  

---

## 📸 Preview

> _(Insert a screenshot showing palette selection and grid with colored cells + hex values)_  
> _(Optional: show Edit Palette popup + Add Palette popup)_

---

## 🧪 Tested With

- ✅ Unity **2021.3 LTS**  
- ✅ Unity **2022.3 LTS**  
- ✅ Unity **2023.1+**  
- 💡 Compatible with **URP**, **HDRP**, and **Built-in RP**  
- 💡 Cross-platform: **Windows**, **macOS**, **Linux**

---

## 💡 Usage Tips

- 🎨 Color palettes are saved in:  
  `ColorUniverse/Resources/ColorUniversePalettes.json`

- 🧠 **Auto-save** triggers when changing palette, editing colors, or closing the window

- 🖱️ Click a color to **copy hex code**, right-click to **open a picker**

- 💬 Use the **Edit or Delete Palette** popup to rename or remove palettes

- ➕ Add new palettes via the **Add Palette** popup

---

## 🧩 Assembly Definition (Optional)

The tool supports `.asmdef` for modular usage.

To enable:
1. Right-click on the `ColorUniverse` folder  
2. Select `Create > Assembly Definition`  
3. Name it `ColorUniverse`  
4. Reference it from your main editor assembly, if needed

---

## 🧃 Developed by

**exwitcher** — for [Compile & Co.](#)  

---

## ❤️ Like this tool?

- ⭐ Star it on GitHub  
- 🐛 Found a bug? Open an issue  
- ✨ Got feedback? Reach out or fork it!  

---
