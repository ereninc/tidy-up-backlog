using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

public static class Extensions
{
	#region [ PROPERTIES ] 

	public static float GetRandom(this float value, float min, float max)
	{
		return UnityEngine.Random.Range(min, max);
	}

	public static int GetRandom(this int value, int min, int max)
	{
		return UnityEngine.Random.Range(min, max);
	}

	public static long Lerp(this long value, long target, float t)
	{
		float val = 1 - t;

		if (value < target)
		{
			if (value >= target * val)
			{
				return target;
			}
		}
		else
		{
			if (value <= target * val)
			{
				return target;
			}
		}

		return (long)((1 - t) * (float)value + t * (float)target);
	}

	public static int Lerp(this int value, int target, float t)
	{
		float val = 1 - t;

		if (value < target)
		{
			if (value >= target * val)
			{
				return target;
			}
		}
		else
		{
			if (value <= target * val)
			{
				return target;
			}
		}

		return (int)((1 - t) * (float)value + t * (float)target);
	}

	public static float Lerp(this float value, float target, float t)
	{
		return Mathf.Lerp(value, target, t);
	}

	public static string ConvertFloatToTime(this float time)
	{
		int minutes = Mathf.FloorToInt(time / 60F);
		int seconds = Mathf.FloorToInt(time - minutes * 60);
		string timeStr = $"{minutes:00}:{seconds:00}";
		return timeStr;
	}

	public static float ToFloat(this string value)
	{
		float val = 0;
		float.TryParse(value, out val);
		return val;
	}

	public static int ToInt(this string value)
	{
		int val = 0;
		int.TryParse(value, out val);
		return val;
	}

	public static string ToCoinValues(this int value)
	{
		if (value > 999999999 || value < -999999999)
		{
			return value.ToString("0,,,.###B", System.Globalization.CultureInfo.InvariantCulture);
		}
		else if (value > 999999 || value < -999999)
		{
			return value.ToString("0,,.##M", System.Globalization.CultureInfo.InvariantCulture);
		}
		else
		{
			return value.ToString("n0");
		}
	}

	public static string ToCoinValues(this long value)
	{
		if (value > 999999999 || value < -999999999)
		{
			return value.ToString("0,,,.###B", System.Globalization.CultureInfo.InvariantCulture);
		}
		else if (value > 999999 || value < -999999)
		{
			return value.ToString("0,,.##M", System.Globalization.CultureInfo.InvariantCulture);
		}
		else
		{
			return value.ToString("n0");
		}
	}

	#endregion

	#region [ UNITY PROPERTIES ] 

	public static ParticleSystem SetStartColor(this ParticleSystem particleSystem, Color color)
	{
		var main = particleSystem.main;
		main.startColor = color;
		return particleSystem;
	}

	public static Texture2D ToTexture2D(this Texture texture)
	{
		return Texture2D.CreateExternalTexture(
			texture.width,
			texture.height,
			TextureFormat.RGBA32,
			false, false,
			texture.GetNativeTexturePtr());
	}

	public static Color Invert(this Color color)
	{
		return Helpers.Colors.InvertColor(color);
	}

	public static Color32 Invert(this Color32 color)
	{
		return Helpers.Colors.InvertColor(color);
	}

	public static float FillImage(this Image target, float value, float maxValue)
	{
		target.fillAmount = value / maxValue;
		return value / maxValue;
	}

	public static float GetRandom(this Vector2 val)
	{
		return UnityEngine.Random.Range(val.x, val.y);
	}

	public static bool HasComponent<T>(this Component gameObject)
	{
		return gameObject.GetComponent<T>() != null;
	}

	public static void SetActiveGameObject(this Component comp, bool statue)
	{
		comp.gameObject.SetActive(statue);
	}

	public static Transform ResetLocal(this Transform target, float scaleFactor = 1f)
	{
		target.localPosition = Vector3.zero;
		target.localRotation = Quaternion.identity;
		target.localScale = Vector3.one * scaleFactor;
		return target;
	}

	public static Transform SetPositionAndRotation(this Transform target, Vector3 localPosition, Quaternion localRotation, Vector3 localScale)
	{
		target.transform.position = localPosition;
		target.transform.localRotation = localRotation;
		target.localScale = localScale;
		return target;
	}

	public static Text SetText(this Button btn, string value)
	{
		if (btn.GetComponentInChildren<Text>() != null)
		{
			btn.GetComponentInChildren<Text>().text = value;
			return btn.GetComponentInChildren<Text>();
		}
		else
		{
			if (btn.GetComponent<Text>() != null)
			{
				btn.GetComponent<Text>().text = value;
				return btn.GetComponent<Text>();
			}
			else
			{
				return null;
			}
		}
	}

	public static Vector3 IsNan(this Vector3 val, Vector3 defaultValue)
	{
		if (float.IsNaN(val.x) || float.IsInfinity(val.x))
		{
			val.x = defaultValue.x;
		}

		if (float.IsNaN(val.y) || float.IsInfinity(val.y))
		{
			val.y = defaultValue.y;
		}

		if (float.IsNaN(val.z) || float.IsInfinity(val.z))
		{
			val.z = defaultValue.z;
		}

		return val;
	}

	public static Transform LerpRotation(this Transform target, Quaternion targetRot, float t, bool lockX, bool lockY,
		bool lockZ)
	{
		Vector3 targetEuler = targetRot.eulerAngles;
		if (lockX)
			targetEuler.x = target.localEulerAngles.x;

		if (lockY)
			targetEuler.y = target.localEulerAngles.y;

		if (lockZ)
			targetEuler.z = target.localEulerAngles.z;


		target.rotation = Quaternion.Lerp(target.rotation, Quaternion.Euler(targetEuler), t);
		return target;
	}

	#endregion

	#region [ COLLECTIONS ] 

	public static T AddItem<T>(this List<T> list, T item)
	{
		if (list == null)
			list = new List<T>();

		list.Add(item);

		return item;
	}

	public static T GetLastItem<T>(this List<T> list)
	{
		return list[list.Count - 1];
	}

	public static T GetLastItem<T>(this T[] list)
	{
		return list[list.Length - 1];
	}

	public static List<T> CreateOrClear<T>(this List<T> list)
	{
		if (list == null)
		{
			list = new List<T>();
		}
		else
		{
			list.Clear();
		}

		return list;
	}

	public static T Find<T>(this T[] array, Predicate<T> match)
	{
		for (int i = 0; i < array.Length; i++)
		{
			if (match(array[i]))
			{
				return array[i];
			}
		}

		return default;
	}

	public static List<T> FindAll<T>(this T[] array, Predicate<T> match)
	{
		List<T> list = new List<T>();
		for (int i = 0; i < array.Length; i++)
		{
			if (match(array[i]))
			{
				list.Add(array[i]);
			}
		}

		return list;
	}

	public static T GetRandom<T>(this IEnumerable<T> values)
	{
		return values.ElementAt(UnityEngine.Random.Range(0, values.Count()));
	}

	public static T GetRandomWithLuck<T>(this List<T> values, List<float> lucks)
	{
		int index = (int)Helpers.Maths.GetItemByLuck(lucks);
		return values[index];
	}

	public static T GetRandomWithLuck<T>(this List<T> values, float[] lucks)
	{
		int index = (int)Helpers.Maths.GetItemByLuck(lucks);
		return values[index];
	}

	public static T GetRandomWithCondition<T>(this List<T> list, Func<T, bool> condition)
	{
		return list.Where(condition).GetRandom();
	}

	public static T GetRandomWithCondition<T>(this T[] list, Func<T, bool> condition)
	{
		return list.Where(condition).GetRandom();
	}

	public static T GetRandom<T>(this List<T> list)
	{
		return list[UnityEngine.Random.Range(0, list.Count)];
	}

	public static T GetRandom<T>(this T[] list)
	{
		return list[UnityEngine.Random.Range(0, list.Length)];
	}

	public static int GetRandomIndex<T>(this T[] list)
	{
		return UnityEngine.Random.Range(0, list.Length);
	}

	public static int GetRandomIndex<T>(this List<T> list)
	{
		return UnityEngine.Random.Range(0, list.Count);
	}

	public static T[] GetRandomItems<T>(this T[] list, int count)
	{
		T[] arrays = new T[count];
		int diff = list.Length / count;
		int startIndex = 0;

		for (int i = 0; i < count; i++)
		{
			int endIndex = (i * diff) + diff;
			int index = UnityEngine.Random.Range(startIndex, endIndex);
			arrays[i] = list[index];
			startIndex = endIndex;
		}

		return arrays;
	}

	public static List<T> GetRandomItems<T>(this List<T> list, int count)
	{
		List<T> arrays = new List<T>();
		int diff = list.Count / count;
		int startIndex = 0;

		for (int i = 0; i < count; i++)
		{
			int endIndex = (i * diff) + diff;
			int index = UnityEngine.Random.Range(startIndex, endIndex);
			arrays.Add(list[index]);
			startIndex = endIndex;
		}

		return arrays;
	}

	public static T[] ShuffleArray<T>(this T[] list)
	{
		System.Random r = new System.Random();
		for (int i = 0; i < list.Length; i++)
		{
			var obj = list[i];
			int index = r.Next(0, list.Length);
			var randomObj = list[index];
			list[index] = obj;
			list[i] = randomObj;
		}

		return list;
	}

	public static List<T> ShuffleList<T>(this List<T> list)
	{
		System.Random r = new System.Random();
		for (int i = 0; i < list.Count; i++)
		{
			var obj = list[i];
			list.Remove(obj);
			int index = r.Next(0, list.Count);
			list.Insert(index, obj);
		}

		return list;
	}

	public static List<T> SetIndex<T>(this List<T> list, int index, int targetIndex)
	{
		if (targetIndex < list.Count)
		{
			T targetObj = list[targetIndex];
			T obj = list[index];

			list[targetIndex] = obj;
			list[index] = targetObj;
		}

		return list;
	}

	public static T[] SetIndex<T>(this T[] list, int index, int targetIndex)
	{
		if (targetIndex < list.Length)
		{
			T targetObj = list[targetIndex];
			T obj = list[index];

			list[targetIndex] = obj;
			list[index] = targetObj;
		}

		return list;
	}

	#endregion

	#region [ AUDIO ]

	public static bool IsReversePitch(this AudioSource source)
	{
		return source.pitch < 0f;
	}

	public static float GetClipRemainingTime(this AudioSource source)
	{
		// Calculate the remainingTime of the given AudioSource,
		// if we keep playing with the same pitch.
		float remainingTime = (source.clip.length - source.time) / source.pitch;
		return source.IsReversePitch() ?
			(source.clip.length + remainingTime) :
			remainingTime;
	}

	#endregion

	#region [ TIMER TEXT ]

	//FLOAT TO MIN:SEC FORMAT
	public static string FormatTime(float timeInSeconds)
	{
		int minutes = Mathf.FloorToInt(timeInSeconds / 60);
		int seconds = Mathf.FloorToInt(timeInSeconds % 60);

		return string.Format("{0:00}:{1:00}", minutes, seconds);
	}

	#endregion

	#region [ STRING ]

	// Emojileri ve özel sembolleri çıkaran fonksiyon
	public static string RemoveEmojis(string input)
	{
		// Unicode aralıkları ile emoji ve sembolleri tanımlayan bir regex
		string emojiPattern = @"[\uD83C-\uDBFF\uDC00-\uDFFF]|[\u2600-\u26FF\u2700-\u27BF\u2300-\u23FF\u2B50-\u2B55]";

		// Regex ile emojileri temizliyoruz
		return Regex.Replace(input, emojiPattern, string.Empty);
	}

	#endregion

	#region [ BOOLEANS ]

	public static bool GetRandomBool()
	{
		var random = UnityEngine.Random.Range(0, 2);
		return random == 0;
	}

	#endregion

	#region [ COLOR ]

	public static Color HexToColor(string hex)
	{
		// Eğer hex kodu "#" işareti içeriyorsa, onu kaldır
		if (hex.StartsWith("#"))
		{
			hex = hex.Substring(1);
		}

		// Renk kodu 6 karakter uzunluğunda olmalıdır
		if (hex.Length != 6)
		{
			Debug.LogError("Hex code should be 6 characters long but it is : " + hex.Length);
			return Color.black;
		}

		// RGB bileşenlerini hexadecimal formatında al
		byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
		byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
		byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);

		// 0-255 aralığındaki RGB bileşenlerini 0-1 aralığına çevir
		return new Color32(r, g, b, 255);
	}

	#endregion

	#region [ ENUMS ]

    public static T RandomEnumValue<T>()
    {
        var values = Enum.GetValues(typeof(T));
        return (T)values.GetValue(UnityEngine.Random.Range(0, values.Length));
    }

    #endregion
}