﻿using System;
using System.Collections.Generic;
using System.Linq;
using RocketLib;
using RocketLib.Utils;

public static class ObjectExtensions
{
    public static void DestroyMe(this UnityEngine.Object target)
    {
        UnityEngine.Object.Destroy(target);
    }

    // Credits: Deadcows/MyBox | Licence: MIT
    /// <summary>
    /// Check if this is a particular type.
    /// </summary>
    public static bool Is<T>(this object source)
    {
        return source is T;
    }

    // Credits: Deadcows/MyBox | Licence: MIT
    /// <summary>
    /// Cast to a different type, exception-safe.
    /// </summary>
    public static T As<T>(this object source) where T : class
    {
        return source as T;
    }

    public static bool NotAs<T>(this object source) where T : class
    {
        return source as T == null;
    }

    public static bool IsTypeOf(this object obj, Type type)
    {
        return obj.GetType() == type;
    }

    /// <summary>
    /// Not great
    /// </summary>
    /// <param name="obj"></param>
    /// <param name="baseType"></param>
    /// <param name="methodName"></param>
    /// <returns></returns>
    /// <exception cref="MissingMethodException"></exception>
    /// <exception cref="Exception"></exception>
    public static void InvokeBaseMethod(this object obj, Type baseType, string methodName)
    {
        obj.InvokeBaseMethod<object>(baseType, methodName);
    }
    /// <summary>
    /// Not great
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="obj"></param>
    /// <param name="baseType"></param>
    /// <param name="methodName"></param>
    /// <returns></returns>
    /// <exception cref="MissingMethodException"></exception>
    /// <exception cref="Exception"></exception>
    public static T InvokeBaseMethod<T>(this object obj, Type baseType, string methodName) where T : class
    {
        var type = obj.GetType();

        var method = baseType.GetMethod(methodName);
        if (method == null)
            throw new MissingMethodException(methodName);
        if (type == baseType)
        {
            return method.Invoke(obj, null) as T;
        }
        if (!type.IsSubclassOf(baseType))
            throw new Exception($"{type} is not a subclass of {baseType}");

        var ptr = method.MethodHandle.GetFunctionPointer();
        var baseMethod = (Func<T>)Activator.CreateInstance(typeof(Func<T>), obj, ptr);
        return baseMethod.Invoke() as T;
    }

    /// <summary>
    /// Compares two objects and prints all differences to the log.
    /// </summary>
    /// <typeparam name="T">The type of objects being compared</typeparam>
    /// <param name="obj1">The first object to compare</param>
    /// <param name="obj2">The second object to compare</param>
    public static void PrintDifferences<T>(this T obj1, T obj2)
    {
        try
        {
            RocketMain.Logger.Log($"Starting comparison of type {typeof(T).Name}");
            var differences = ObjectComparer.Compare(obj1, obj2);

            if (!differences.Any())
            {
                RocketMain.Logger.Log("Objects are identical");
                return;
            }

            RocketMain.Logger.Log($"Found {differences.Count} differences:");

            // Find the longest property path for alignment
            int maxPathLength = differences.Max(d => d.PropertyPath.Length);

            // Print each difference with aligned values
            foreach (var diff in differences)
            {
                var paddedPath = diff.PropertyPath.PadRight(maxPathLength);
                RocketMain.Logger.Log($"  {paddedPath} : '{diff.Value1}' → '{diff.Value2}'");
            }
        }
        catch (Exception ex)
        {
            RocketMain.Logger.Log($"Error during comparison: {ex.Message}");
        }
    }

    /// <summary>
    /// Compares two objects and generates code to make obj1 match obj2.
    /// The generated code uses 'this.' and is meant to be placed inside the class.
    /// </summary>
    /// <typeparam name="T">The type of objects being compared</typeparam>
    /// <param name="obj1">The target objec</param>
    /// <param name="obj2">The source object (values to copy from)t</param>
    public static void GenerateMatchingCode<T>(this T obj1, T obj2)
    {
        try
        {
            RocketMain.Logger.Log($"Generating matching code for type {typeof(T).Name}");
            var differences = ObjectComparer.Compare(obj2, obj1);

            if (!differences.Any())
            {
                RocketMain.Logger.Log("Objects are identical - no matching needed");
                return;
            }

            RocketMain.Logger.Log($"// Code to match {differences.Count} differences:");
            RocketMain.Logger.Log("// Copy and paste the following code inside the target class:");
            RocketMain.Logger.Log("");

            var generatedCode = new List<string>();

            foreach (var diff in differences)
            {
                var matchingCode = ObjectComparer.GenerateMatchingStatement(diff.PropertyPath, diff.Value1);
                if (!string.IsNullOrEmpty(matchingCode))
                {
                    generatedCode.Add(matchingCode);
                }
            }

            if (generatedCode.Any())
            {
                foreach (var code in generatedCode)
                {
                    RocketMain.Logger.Log(code);
                }
            }
            else
            {
                RocketMain.Logger.Log("// Unable to generate matching code for complex types");
            }
        }
        catch (Exception ex)
        {
            RocketMain.Logger.Log($"Error generating matching code: {ex.Message}");
        }
    }
}
