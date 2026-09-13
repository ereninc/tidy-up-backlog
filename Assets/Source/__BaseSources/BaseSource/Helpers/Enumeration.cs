using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class Enumeration<T> where T : struct
{
	public static List<T> GetList()
	{
		var values = Enum.GetValues(typeof(T));
		return values.Cast<T>().ToList();
	}

	public static bool IsNameExists(string enumName)
	{
		var list = GetList();

		foreach (var item in list)
		{
			if (item.ToString().Equals(enumName))
				return true;
		}
		return false;
	}

	/// <summary>
	/// Gets the corresponding enum if given name exists, if name doesnt exist returns null
	/// </summary>
	/// <param name="enumName">Name of enumeration</param>
	/// <returns>typeof (enum)</returns>
	public static T? GetEnum(string enumName)
	{
		var list = GetList();

		foreach (var item in list)
		{
			if (item.ToString().Equals(enumName))
				return item;
		}
		return null;
	}

	/// <summary>
	/// Gets the corresponding enum if given id exists, if name doesnt exist returns null
	/// </summary>
	/// <param name="enumInt">Value of enumeration</param>
	/// <returns>typeof (enum)</returns>
	public static T? GetEnum(int enumInt)
	{
		var list = GetList();

		foreach (var item in list)
		{
			if (Convert.ToInt16(item) == enumInt)
				return item;
		}
		return null;
	}

	public static T? Next(T src)
	{
		if (!typeof(T).IsEnum) throw new ArgumentException(String.Format("Argument {0} is not an Enum", typeof(T).FullName));

		T[] Arr = (T[])Enum.GetValues(src.GetType());
		int j = Array.IndexOf<T>(Arr, src) + 1;
		return (Arr.Length == j) ? Arr[j - 1] : Arr[j];
	}
	
	public static T Prev(T src)
	{
		if (!typeof(T).IsEnum) throw new ArgumentException(String.Format("Argument {0} is not an Enum", typeof(T).FullName));

		T[] Arr = (T[])Enum.GetValues(src.GetType());
		int j = Array.IndexOf<T>(Arr, src) - 1;
		return (j < 0) ? Arr[0] : Arr[j];
	}
	
	public static T NextLoop(T src)
	{
		if (!typeof(T).IsEnum) throw new ArgumentException(String.Format("Argument {0} is not an Enum", typeof(T).FullName));

		T[] Arr = (T[])Enum.GetValues(src.GetType());
		int j = Array.IndexOf<T>(Arr, src) + 1;
		return (Arr.Length == j) ? Arr[0] : Arr[j];
	}
	
	public static T PrevLoop(T src)
	{
		if (!typeof(T).IsEnum) throw new ArgumentException(String.Format("Argument {0} is not an Enum", typeof(T).FullName));

		T[] Arr = (T[])Enum.GetValues(src.GetType());
		int j = Array.IndexOf<T>(Arr, src) - 1;
		return (j < 0) ? Arr[^1] : Arr[j];
	}
}