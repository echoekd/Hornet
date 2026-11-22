using Modding;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Resources;
using System.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;
using static UnityEngine.Scripting.GarbageCollector;

namespace RadianceUnbound
{
    public class RadianceUnbound : Mod, ITogglableMod
    {
        public bool ToggleButtonInsideMenu => true;
        public override string GetVersion() => "bate";
        public RadianceUnbound() : base("RadianceUnbound") { }

        public override void Initialize(Dictionary<string, Dictionary<string, GameObject>> preloadedObjects)
        {
            On.PlayMakerFSM.Start += BossManager.Instance.OnFSMStart;
        }

        public void Unload()
        {
            On.PlayMakerFSM.Start -= BossManager.Instance.OnFSMStart;
        }
    }
}