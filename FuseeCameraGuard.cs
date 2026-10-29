using System;
using HarmonyLib;
using UnityEngine;

namespace AutoEngineerBrakeFix
{
    /// <summary>
    /// Stops a placed fusee from throwing while the main camera is switched off.
    ///
    /// Effects.FuseeRenderer.Update does filamentRenderer.transform.LookAt(Camera.main.transform, ...) with no null
    /// check. Fast-forward starts a FadeOut (UI.Common.FadeOutController.Show) that sets the main camera's
    /// enabled = false half a second later, and Camera.main only returns an enabled camera, so for the whole
    /// fast-forward every active fusee threw a NullReferenceException on every frame (1,031 of them in one logged
    /// run). FuseeRenderer.UpdateEmission, called right after, already returns when there is no camera.
    ///
    /// This prefix skips the update while there is no main camera. With a camera it does nothing.
    /// </summary>
    [HarmonyPatch(typeof(Effects.FuseeRenderer), "Update", new Type[0])]
    internal static class FuseeRenderer_Update_Patch
    {
        private static bool _logged;

        private static bool Prefix()
        {
            if (!Main.Active || !Main.Settings.GuardFuseeCamera) return true;
            if (Camera.main != null) return true;
            if (!_logged)
            {
                _logged = true;
                try
                {
                    Main.Mod.Logger.Log("Fusee update skipped: there is no main camera (the game switches it off during fast-forward). Further cases are not logged.");
                }
                catch (Exception) { }
            }
            return false;
        }
    }
}
