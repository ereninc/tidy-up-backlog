// TimeTweaker v1.0.0 - by Compile&Co.
// Responsible for constructing and returning the timeScale slider and reset button UI.

using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CompileCo.TimeTweaker.Editor
{
	public static class TimeTweakerToolbarUI
	{
		public static VisualElement Create()
		{
			//find settings file
			var settings = AssetDatabase.FindAssets("t:TimeTweakerSettings")
				.Select(guid => AssetDatabase.LoadAssetAtPath<TimeTweakerSettings>(AssetDatabase.GUIDToAssetPath(guid)))
				.FirstOrDefault();

			float min = settings ? settings.minTimeScale : 0f;
			float max = settings ? settings.maxTimeScale : 2f;
			float def = settings ? settings.defaultTimeScale : 1f;
			
			var container = new VisualElement
			{
				name = "TimeScaleSliderContainer"
			};
			container.style.flexDirection = FlexDirection.Row;
			container.style.alignItems = Align.Center;
			container.style.marginLeft = TimeTweakerToolbarStyle.MarginLeft;
			container.style.height = TimeTweakerToolbarStyle.ContainerHeight;

			var label = new Label(TimeTweakerToolbarText.SliderLabel)
			{
				style =
				{
					marginRight = TimeTweakerToolbarStyle.LabelMarginRight,
					fontSize = 12
				}
			};
			
			var currentTimeScaleText = new Label(TimeTweakerToolbarText.CurrentValue)
			{
				style =
				{
					marginRight = TimeTweakerToolbarStyle.LabelMarginLeft,
					fontSize = 14,
					unityFontStyleAndWeight = FontStyle.Bold,
					minWidth = 36,
					width = 36
				}
			};

			var slider = new Slider(min, max)
			{
				value = Time.timeScale,
				style =
				{
					width = TimeTweakerToolbarStyle.SliderWidth
				}
			};
			
			slider.lowValue = min;
			slider.highValue = max;
			
			float last = settings ? settings.lastKnownTimeScale : def;
			slider.value = Mathf.Clamp(last, min, max);
			
			currentTimeScaleText.text = last.ToString("0.00");
			
			slider.RegisterValueChangedCallback(evt =>
			{
				Time.timeScale = evt.newValue;
				currentTimeScaleText.text = evt.newValue.ToString("0.00");

				if (settings)
				{
					settings.lastKnownTimeScale = evt.newValue;
					EditorUtility.SetDirty(settings);
				}
			});
			
			var resetButton = new Button(() =>
			{
				slider.value = def;
				Time.timeScale = def;
			})
			{
				text = TimeTweakerToolbarText.ResetButton
			};
			
			resetButton.clicked += () =>
			{
				slider.value = def;
				Time.timeScale = def;
				currentTimeScaleText.text = def.ToString("0.00");

				if (settings)
				{
					settings.lastKnownTimeScale = def;
					EditorUtility.SetDirty(settings);
				}
			};
			
			EditorApplication.update += () =>
			{
				if (Mathf.Abs(Time.timeScale - slider.value) > 0.01f)
				{
					slider.SetValueWithoutNotify(Time.timeScale);
					currentTimeScaleText.text = Time.timeScale.ToString("0.00");

					if (settings)
					{
						settings.lastKnownTimeScale = Time.timeScale;
						EditorUtility.SetDirty(settings);
					}
				}
			};

			// Styling — DO NOT CHANGE
			resetButton.style.borderLeftWidth = 2;
			resetButton.style.borderRightWidth = 2;
			resetButton.style.borderTopWidth = 2;
			resetButton.style.borderBottomWidth = 2;

			resetButton.style.borderLeftColor = TimeTweakerToolbarStyle.ResetButtonBorderColor;
			resetButton.style.borderRightColor = TimeTweakerToolbarStyle.ResetButtonBorderColor;
			resetButton.style.borderTopColor = TimeTweakerToolbarStyle.ResetButtonBorderColor;
			resetButton.style.borderBottomColor = TimeTweakerToolbarStyle.ResetButtonBorderColor;

			resetButton.style.minWidth = TimeTweakerToolbarStyle.ResetButtonMinWidth;
			resetButton.style.minHeight = TimeTweakerToolbarStyle.ResetButtonMinHeight;

			resetButton.style.paddingLeft = 0;
			resetButton.style.paddingRight = 0;
			resetButton.style.paddingTop = 0;
			resetButton.style.paddingBottom = 0;
			resetButton.style.overflow = Overflow.Visible;

			resetButton.style.backgroundColor = TimeTweakerToolbarStyle.ResetButtonBackground;
			resetButton.style.color = TimeTweakerToolbarStyle.ResetButtonTextColor;
			resetButton.style.unityFontStyleAndWeight = FontStyle.Bold;

			container.Add(label);
			container.Add(slider);
			container.Add(currentTimeScaleText);
			container.Add(resetButton);
			return container;
		}
	}
}