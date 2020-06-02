﻿using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

#if VIU_XR_GENERAL_SETTINGS
using UnityEditor.XR.Management;
using UnityEngine.XR.Management;
#endif

namespace HTC.UnityPlugin.Vive
{
    public static class XRPluginManagementUtils
    {
        private static readonly string[] s_loaderBlackList = { "DummyLoader", "SampleLoader", "XRLoaderHelper" };
        private static string s_defaultAssetPath;

        public static string defaultAssetPath
        {
            get
            {
                if (s_defaultAssetPath == null)
                {
                    var ms = MonoScript.FromScriptableObject(ScriptableObject.CreateInstance<XRGeneralSettings>());
                    var msPath = AssetDatabase.GetAssetPath(ms);
                    var assetsPath = msPath.Replace(msPath.Substring(0, msPath.LastIndexOf("/")), "Assets/XR");
                    s_defaultAssetPath = System.IO.Path.ChangeExtension(assetsPath, "asset");
                }

                return s_defaultAssetPath;
            }
        }

        public static bool IsXRLoaderEnabled(string loaderName, BuildTargetGroup buildTargetGroup)
        {
#if VIU_XR_GENERAL_SETTINGS
            XRGeneralSettings xrSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(buildTargetGroup);
            if (!xrSettings)
            {
                Debug.LogWarning("Failed to find XRGeneralSettings for build target group: " + buildTargetGroup);
                return false;
            }

            if (!xrSettings.AssignedSettings)
            {
                Debug.LogWarning("No assigned manager settings in the XRGeneralSettings for build target group: " + buildTargetGroup);
                return false;
            }
            
            foreach (XRLoader loader in xrSettings.AssignedSettings.loaders)
            {
                if (loader.name == loaderName)
                {
                    return true;
                }
            }
#endif
            return false;
        }

        public static bool IsAnyXRLoaderEnabled(BuildTargetGroup buildTargetGroup)
        {
#if VIU_XR_GENERAL_SETTINGS
            XRGeneralSettings xrSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(buildTargetGroup);
            if (!xrSettings)
            {
                Debug.LogWarning("Failed to find XRGeneralSettings for build target group: " + buildTargetGroup);
                return false;
            }

            if (!xrSettings.AssignedSettings)
            {
                Debug.LogWarning("No assigned manager settings in the XRGeneralSettings for build target group: " + buildTargetGroup);
                return false;
            }

            return xrSettings.AssignedSettings.loaders.Count > 0;
#else
            return false;
#endif
        }

        public static void SetXRLoaderEnabled(string loaderClassName, BuildTargetGroup buildTargetGroup, bool enabled)
        {
#if VIU_XR_GENERAL_SETTINGS
            XRGeneralSettingsPerBuildTarget generalSettings = null;
            if (!File.Exists(defaultAssetPath))
            {
                if (!Directory.Exists(defaultAssetPath))
                {
                    Directory.CreateDirectory(defaultAssetPath);
                }

                generalSettings = ScriptableObject.CreateInstance(typeof(XRGeneralSettingsPerBuildTarget)) as XRGeneralSettingsPerBuildTarget;
                AssetDatabase.CreateAsset(generalSettings, defaultAssetPath);
            }
            else
            {
                EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out generalSettings);
                if (generalSettings == null)
                {
                    string searchText = "t:XRGeneralSettings";
                    string[] assets = AssetDatabase.FindAssets(searchText);
                    if (assets.Length > 0)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(assets[0]);
                        generalSettings = AssetDatabase.LoadAssetAtPath(path, typeof(XRGeneralSettingsPerBuildTarget)) as XRGeneralSettingsPerBuildTarget;
                    }
                }
            }

            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, generalSettings, true);

            XRGeneralSettings xrSettings = generalSettings.SettingsForBuildTarget(buildTargetGroup);

            if (xrSettings == null)
            {
                xrSettings = ScriptableObject.CreateInstance<XRGeneralSettings>() as XRGeneralSettings;
                generalSettings.SetSettingsForBuildTarget(buildTargetGroup, xrSettings);
                xrSettings.name = $"{buildTargetGroup.ToString()} Settings";
                AssetDatabase.AddObjectToAsset(xrSettings, AssetDatabase.GetAssetOrScenePath(generalSettings));
            }

            var serializedSettingsObject = new SerializedObject(xrSettings);
            serializedSettingsObject.Update();

            SerializedProperty loaderProp = serializedSettingsObject.FindProperty("m_LoaderManagerInstance");
            if (loaderProp.objectReferenceValue == null)
            {
                var xrManagerSettings = ScriptableObject.CreateInstance<XRManagerSettings>() as XRManagerSettings;
                xrManagerSettings.name = $"{buildTargetGroup.ToString()} Providers";
                AssetDatabase.AddObjectToAsset(xrManagerSettings, AssetDatabase.GetAssetOrScenePath(generalSettings));
                loaderProp.objectReferenceValue = xrManagerSettings;
                serializedSettingsObject.ApplyModifiedProperties();
            }

            var obj = loaderProp.objectReferenceValue;

            if (obj == null)
            {
                xrSettings.AssignedSettings = null;
                loaderProp.objectReferenceValue = null;
            }

            serializedSettingsObject.ApplyModifiedProperties();

            if (enabled)
            {
#if VIU_XR_PACKAGE_METADATA_STORE
                if (!UnityEditor.XR.Management.Metadata.XRPackageMetadataStore.AssignLoader(xrSettings.AssignedSettings, loaderClassName, buildTargetGroup))
                {
                    Debug.LogWarning("Failed to assign XR loader: " + loaderClassName);
                }
#else
                if (!AssignLoader(xrSettings.AssignedSettings, loaderClassName))
                {
                    Debug.LogWarning("Failed to assign XR loader: " + loaderClassName);
                }
#endif
            }
            else
            {
#if VIU_XR_PACKAGE_METADATA_STORE
                if (!UnityEditor.XR.Management.Metadata.XRPackageMetadataStore.RemoveLoader(xrSettings.AssignedSettings, loaderClassName, buildTargetGroup))
                {
                    Debug.LogWarning("Failed to remove XR loader: " + loaderClassName);
                }
#else
                if (!RemoveLoader(xrSettings.AssignedSettings, loaderClassName))
                {
                    Debug.LogWarning("Failed to remove XR loader: " + loaderClassName);
                }
#endif
            }
#endif
        }

#if VIU_XR_GENERAL_SETTINGS
        private static bool AssignLoader(XRManagerSettings settings, string loaderTypeName)
        {
            var instance = GetInstanceOfTypeWithNameFromAssetDatabase(loaderTypeName);
            if (instance == null || !(instance is XRLoader))
            {
                instance = CreateScriptableObjectInstance(loaderTypeName, GetAssetPathForComponents(new string[] {"XR", "Loaders"}));
                if (instance == null)
                    return false;
            }

            List<XRLoader> assignedLoaders = new List<XRLoader>(settings.loaders);
            XRLoader newLoader = instance as XRLoader;

            if (!assignedLoaders.Contains(newLoader))
            {
                assignedLoaders.Add(newLoader);
                settings.loaders.Clear();

                List<string> allLoaderTypeNames = GetAllLoaderTypeNames();
                foreach (var typeName in allLoaderTypeNames)
                {
                    var newInstance = GetInstanceOfTypeWithNameFromAssetDatabase(typeName) as XRLoader;

                    if (newInstance != null && assignedLoaders.Contains(newInstance))
                    {
                        settings.loaders.Add(newInstance);
                    }
                }

                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            return true;
        }

        private static bool RemoveLoader(XRManagerSettings settings, string loaderTypeName)
        {
            var instance = GetInstanceOfTypeWithNameFromAssetDatabase(loaderTypeName);
            if (instance == null || !(instance is XRLoader))
                return false;

            XRLoader loader = instance as XRLoader;

            if (settings.loaders.Contains(loader))
            {
                settings.loaders.Remove(loader);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            return true;
        }

        private static ScriptableObject GetInstanceOfTypeWithNameFromAssetDatabase(string typeName)
        {
            string[] assetGUIDs = AssetDatabase.FindAssets(string.Format("t:{0}", typeName));
            if (assetGUIDs.Any())
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGUIDs[0]);
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath(assetPath, typeof(ScriptableObject));

                return asset as ScriptableObject;
            }

            return null;
        }

        private static ScriptableObject CreateScriptableObjectInstance(string typeName, string path)
        {
            ScriptableObject obj = ScriptableObject.CreateInstance(typeName) as ScriptableObject;
            if (obj != null)
            {
                if (!string.IsNullOrEmpty(path))
                {
                    string fileName = string.Format("{0}.asset", TypeNameToString(typeName));
                    string targetPath = Path.Combine(path, fileName);
                    AssetDatabase.CreateAsset(obj, targetPath);

                    return obj;
                }
            }

            Debug.LogError($"We were unable to create an instance of the requested type {typeName}. Please make sure that all packages are updated to support this version of XR Plug-In Management. See the Unity documentation for XR Plug-In Management for information on resolving this issue.");

            return null;
        }

        private static string GetAssetPathForComponents(string[] pathComponents, string root = "Assets")
        {
            if (pathComponents.Length <= 0)
                return null;

            string path = root;
            foreach( var pc in pathComponents)
            {
                string subFolder = Path.Combine(path, pc);
                bool shouldCreate = true;
                foreach (var f in AssetDatabase.GetSubFolders(path))
                {
                    if (string.Compare(Path.GetFullPath(f), Path.GetFullPath(subFolder), true) == 0)
                    {
                        shouldCreate = false;
                        break;
                    }
                }

                if (shouldCreate)
                    AssetDatabase.CreateFolder(path, pc);
                path = subFolder;
            }

            return path;
        }

        private static string TypeNameToString(Type type)
        {
            return type == null ? "" : TypeNameToString(type.FullName);
        }

        private static string TypeNameToString(string type)
        {
            string[] typeParts = type.Split(new char[] { '.' });
            if (!typeParts.Any())
                return String.Empty;

            string[] words = Regex.Matches(typeParts.Last(), "(^[a-z]+|[A-Z]+(?![a-z])|[A-Z][a-z]+)")
                .OfType<Match>()
                .Select(m => m.Value)
                .ToArray();
            return string.Join(" ", words);
        }

        private static List<string> GetAllLoaderTypeNames()
        {
            List<string> loaderTypeNames = new List<string>();
            var loaderTypes = TypeCache.GetTypesDerivedFrom(typeof(XRLoader));
            foreach (Type loaderType in loaderTypes)
            {
                if (loaderType.IsAbstract)
                    continue;

                if (s_loaderBlackList.Contains(loaderType.Name))
                    continue;

                loaderTypeNames.Add(loaderType.Name);
            }

            return loaderTypeNames;
        }
#endif
    }
}