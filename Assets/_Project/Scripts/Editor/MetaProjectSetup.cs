using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace StackTower.EditorTools
{
    /// <summary>
    /// Runs Meta's Project Setup Tool the way its window does: "Fix All" (required items) and
    /// "Apply All" (recommended items), and nothing else.
    ///
    /// The SDK's public entry point, OVRProjectSetup.FixAllAsync, also applies every optional
    /// suggestion (for example it switches on Application SpaceWarp), which the window's buttons
    /// don't. Filtering by level needs the same internal call the window makes, so this reaches it
    /// by reflection and reports failure instead of guessing if a future SDK moves things around.
    /// </summary>
    internal static class MetaProjectSetup
    {
        public sealed class Run
        {
            public bool Completed;
        }

        private static PropertyInfo _level;
        private static MethodInfo _levelValue;
        private static BuildTargetGroup _group;

        /// <returns>A handle to poll, or null when the tool couldn't be driven (use its window instead).</returns>
        public static Run FixRequiredAndRecommended(BuildTargetGroup group)
        {
            try
            {
                Type setup = typeof(OVRProjectSetup);
                Type taskType = setup.Assembly.GetType("OVRConfigurationTask");
                Type processorType = setup.Assembly.GetType("OVRConfigurationTaskProcessor");
                MethodInfo fixTasks = setup.GetMethod("FixTasks", BindingFlags.Static | BindingFlags.NonPublic);
                if (taskType == null || processorType == null || fixTasks == null)
                {
                    return null;
                }

                const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                _level = taskType.GetProperty("Level", instance);
                _levelValue = _level?.PropertyType.GetMethod("GetValue", new[] { typeof(BuildTargetGroup) });
                if (_levelValue == null)
                {
                    return null;
                }

                _group = group;

                const BindingFlags helpers = BindingFlags.Static | BindingFlags.NonPublic;
                Type filterType = typeof(Func<,>).MakeGenericType(
                    typeof(IEnumerable<>).MakeGenericType(taskType), typeof(List<>).MakeGenericType(taskType));
                Delegate filter = Delegate.CreateDelegate(filterType,
                    typeof(MetaProjectSetup).GetMethod(nameof(RequiredAndRecommended), helpers).MakeGenericMethod(taskType));

                var run = new Run();
                object onCompleted = typeof(MetaProjectSetup).GetMethod(nameof(CompletionCallback), helpers)
                    .MakeGenericMethod(processorType).Invoke(null, new object[] { run });

                ParameterInfo[] parameters = fixTasks.GetParameters();
                var arguments = new object[parameters.Length];
                for (int i = 0; i < parameters.Length; i++)
                {
                    ParameterInfo parameter = parameters[i];
                    switch (parameter.Name)
                    {
                        case "buildTargetGroup":
                            arguments[i] = group;
                            break;
                        case "filter":
                            arguments[i] = filter;
                            break;
                        case "blocking":
                            arguments[i] = false;
                            break;
                        case "onCompleted":
                            arguments[i] = onCompleted;
                            break;
                        default:
                            if (!parameter.HasDefaultValue)
                            {
                                return null;
                            }

                            arguments[i] = parameter.ParameterType.IsEnum
                                ? Enum.ToObject(parameter.ParameterType, parameter.DefaultValue ?? 0)
                                : parameter.DefaultValue;
                            break;
                    }
                }

                fixTasks.Invoke(null, arguments);
                return run;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Stack Tower] Could not drive Meta's Project Setup Tool: " + e.Message);
                return null;
            }
        }

        private static List<T> RequiredAndRecommended<T>(IEnumerable<T> tasks)
        {
            var kept = new List<T>();
            foreach (T task in tasks)
            {
                object level = _levelValue.Invoke(_level.GetValue(task), new object[] { _group });
                if (level is OVRProjectSetup.TaskLevel taskLevel && taskLevel >= OVRProjectSetup.TaskLevel.Recommended)
                {
                    kept.Add(task);
                }
            }

            return kept;
        }

        private static Action<T> CompletionCallback<T>(Run run)
        {
            return _ => run.Completed = true;
        }
    }
}
