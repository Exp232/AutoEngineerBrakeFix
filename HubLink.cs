// Link to the Mod Settings Hub (exp232/modsettingshub), by reflection: this mod works the same without it.
// Template and contract: HubLink.Template.cs in the hub's repo.

using System;
using System.Reflection;
using UI.Builder;

namespace AutoEngineerBrakeFix
{
    internal static class HubLink
    {
        private const string HubAssembly = "ModSettingsHub";
        private const string HubType = "ModSettingsHub.Hub";

        private static string _id;
        private static string _title;
        private static string _version;
        private static Action<UIPanelBuilder> _build;
        private static bool _registered;
        private static bool _watching;

        /// <summary>True once the hub has taken the page.</summary>
        internal static bool Registered
        {
            get { return _registered; }
        }

        internal static void Register(string id, string title, string version, Action<UIPanelBuilder> build)
        {
            _id = id;
            _title = title;
            _version = version;
            _build = build;
            if (!_watching)
            {
                _watching = true;
                AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;   // the hub may load after this mod
            }
            TryRegister();
        }

        internal static void Unregister()
        {
            if (_watching)
            {
                _watching = false;
                AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;
            }
            if (_registered)
            {
                _registered = false;
                Call("Unregister", new[] { typeof(string) }, new object[] { _id });
            }
        }

        /// <summary>Shows or hides this mod's page in the hub. False when the hub is missing or could not do it.</summary>
        internal static bool Toggle()
        {
            if (!_registered) return false;
            object handled = Call("ToggleFor", new[] { typeof(string) }, new object[] { _id });
            return handled is bool && (bool)handled;
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            if (args.LoadedAssembly.GetName().Name == HubAssembly) TryRegister();
        }

        private static void TryRegister()
        {
            if (_registered || _build == null) return;
            object done = Call("Register",
                new[] { typeof(string), typeof(string), typeof(string), typeof(Action<UIPanelBuilder>) },
                new object[] { _id, _title, _version, _build });
            _registered = done != null;
        }

        // Returns null when the hub is not there or the call failed; a non-null result otherwise.
        private static object Call(string method, Type[] parameters, object[] args)
        {
            try
            {
                Type hub = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.GetName().Name == HubAssembly)
                    {
                        hub = assembly.GetType(HubType, false);
                        break;
                    }
                }
                if (hub == null) return null;
                MethodInfo m = hub.GetMethod(method, BindingFlags.Public | BindingFlags.Static, null, parameters, null);
                if (m == null) return null;
                object result = m.Invoke(null, args);
                return result ?? true;   // void methods report success as true
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
