# TimeTweaker – Fast Iteration

**TimeTweaker** is a lightweight Unity Editor utility that adds a runtime time scale slider directly to the play mode toolbar, allowing you to test your game at different speeds effortlessly.

---

## ✨ Features

- 🕹️ Adjust `Time.timeScale` during play mode with a simple slider
- 🔁 Reset time scale to default with a single click
- ⚙️ Customizable range and default value via ScriptableObject
- ⚡ Fast, responsive, and Editor-only

---

## 📦 Installation

Drop the `TimeTweaker` folder into your Unity project's `Assets/` directory.

> ✅ Editor-only: No runtime code is included in builds.

---

## 🔧 Customization

You can create a custom settings asset to control the min/max/default values of the slider:

1. Right-click in Project Window → `Create > CompileCo > TimeTweaker > Settings`
2. Adjust:
   - **Min Time Scale**
   - **Max Time Scale**
   - **Default Time Scale**
3. The toolbar UI will automatically update when values are changed.

---

## 🧪 Use Case

Speed up or slow down gameplay to:
- Test cutscenes, movement, or physics at different time scales
- Debug frame-sensitive mechanics
- Create slow-motion test scenarios

---

## 🧠 Tip

Keep a settings asset in a `Resources/` folder if you'd like it to be automatically found across projects.

---

## 🏷️ About

**Developed by:** [Compile & Co.](https://compile-co.github.io)  
**Version:** 1.0.0  
**License:** MIT  
