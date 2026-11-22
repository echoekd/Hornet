using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using Modding;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Resources;
using UnityEngine;
using Vasi;

namespace RadianceUnbound
{
    public class BossManager 
    {
        private static readonly BossManager instance = new BossManager();
        public static BossManager Instance
        {
            get { return instance; }
        }
        public void OnFSMStart(On.PlayMakerFSM.orig_Start orig, PlayMakerFSM self)
        {
            orig(self);
            if (self.gameObject.name == "Hornet Boss 1" &&
                self.FsmName == "Control" &&
                self.gameObject.scene.name == "Fungus1_04_boss")
            {
                if (self.gameObject.GetComponent<Hornet1StormBehavior>() == null)
                {
                    self.gameObject.AddComponent<Hornet1StormBehavior>();
                }
                var throwAnticState = self.FsmStates.FirstOrDefault(s => s.Name == "Throw Antic");
                if (throwAnticState != null)
                {
                    foreach (var action in throwAnticState.Actions.OfType<Wait>())
                    {
                        action.time.Value *= 0.3f; // 前摇时间减半
                    }
                }
                var gdash = self.FsmStates.FirstOrDefault(s => s.Name == "G Dash");
                if (gdash != null)
                {
                    foreach (var a in gdash.Actions)
                    {
                        if (a is SetVelocity2d sv)
                        {
                            float origX = sv.x.Value;
                            sv.x.Value = Mathf.Sign(origX) * Mathf.Abs(origX) * 1.5f;
                        }
                        else if (a is Wait w)
                        {
                            w.time.Value *= 1.2f;
                        }
                    }
                }
                var hm = self.GetComponent<HealthManager>();
                if (hm != null) hm.hp = 500;
                var stunFsm = self.gameObject.LocateMyFSM("Stun Control");
                if (stunFsm != null) UnityEngine.Object.Destroy(stunFsm);
                var recoil = self.gameObject.GetComponent<Recoil>();
                if (recoil != null) recoil.enabled = false;
            }
        }

        public class Hornet1StormBehavior : MonoBehaviour
        {
            public const float StormDuration = 3f;
            public const int ExtraStormCount = 2;
            public const float FadeInTime = 1f;
            public const float ExtraLiveTime = 2f;

            private PlayMakerFSM _control;
            private GameObject _sphereG, _sphereA;
            private bool _holding;
            private string _lastState = "";
            private readonly List<GameObject> _extraStorms = new List<GameObject>();
            private int _comboStep = 0;
            private bool _plungeArm = false;
            private float _inAirTime = -1f;

            void Start()
            {
                _control = GetComponents<PlayMakerFSM>().FirstOrDefault(f => f.FsmName == "Control");
                if (_control == null) { enabled = false; return; }
                _sphereG = StripAndGrab("Sphere Recover");
                _sphereA = StripAndGrab("Sphere Recover A");
                if (_sphereG == null && _sphereA == null)
                {
                    enabled = false; return;
                }
                foreach (var s in new[] { _sphereG, _sphereA })
                {
                    if (s == null) continue;
                    var grow = s.GetComponents<PlayMakerFSM>().FirstOrDefault(f => f.FsmName == "Grow");
                    var st = grow?.FsmStates.FirstOrDefault(x => x.Name == "Grow");
                    if (st != null && st.Actions.Length > 0)
                        st.Actions = st.Actions.Skip(1).ToArray();
                }
            }
            private GameObject StripAndGrab(string stateName)
            {
                var st = _control.FsmStates.FirstOrDefault(x => x.Name == stateName);
                if (st == null) return null;
                GameObject sphere = null;
                var keep = new List<FsmStateAction>();
                foreach (var a in st.Actions)
                {
                    if (a is ActivateGameObject act && !act.activate.Value && act.gameObject.GameObject.Value != null)
                        sphere = act.gameObject.GameObject.Value;
                    else keep.Add(a);
                }
                st.Actions = keep.ToArray();
                return sphere;
            }
            void Update()
            {
                if (_control == null) return;
                var cur = _control.ActiveStateName;
                bool changed = cur != _lastState;
                if (!_holding && (cur == "Sphere" || cur == "Sphere A"))
                {
                    var s = cur == "Sphere" ? _sphereG : _sphereA;
                    if (s != null) StartCoroutine(Hold(s));
                    StartCoroutine(SpawnExtraStorms());
                }
                if (changed)
                {
                    if (cur == "GDash Recover2")
                    {
                        _comboStep++;
                        if (_comboStep == 1)
                        {
                            _control.SetState("GDash Antic");   // 第二刀
                        }
                        else if (_comboStep == 2)
                        {
                            _plungeArm = true; _inAirTime = -1f;
                            _control.SetState("Jump Antic");    // 下戳起跳
                        }
                    }
                    if (_plungeArm && cur == "In Air" && _inAirTime < 0)
                        _inAirTime = Time.time;
                    if (_plungeArm && _inAirTime >= 0 && cur != "In Air"
                        && cur != "Jump Antic" && cur != "Aim Jump" && cur != "Jump")
                    { _plungeArm = false; _inAirTime = -1f; }
                    if (cur == "Hard Land" || cur == "Land" || cur == "Idle"
                        || cur.Contains("Stun") || cur.Contains("Move Choice"))
                    { _comboStep = 0; _plungeArm = false; _inAirTime = -1f; }

                    _lastState = cur;
                }
                if (_plungeArm && _inAirTime >= 0 && cur == "In Air" && Time.time - _inAirTime >= 0.12f)
                {
                    var hero = HeroController.instance;
                    if (hero != null)
                    {
                        float needY = hero.transform.position.y + 2.0f;
                        if (transform.position.y < needY)
                            transform.position = new Vector3(transform.position.x, needY, transform.position.z);
                    }
                    _control.SendEvent("AIRDASH");
                    _plungeArm = false; _inAirTime = -1f;
                }
            }
            private IEnumerator SpawnExtraStorms()
            {
                var prefab = _sphereG != null ? _sphereG : _sphereA;
                var hero = HeroController.instance;
                if (prefab == null || hero == null) yield break;
                for (int i = 0; i < ExtraStormCount; i++)
                {
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    float dist = Random.Range(2f, 4f);
                    Vector3 pos = hero.transform.position +
                        new Vector3(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist * 0.6f, 0);
                    var clone = Instantiate(prefab, pos, Quaternion.identity);
                    clone.name = "ExtraSilkStorm_" + i;
                    _extraStorms.Add(clone);
                    StartCoroutine(FadeStorm(clone));
                    yield return new WaitForSeconds(0.15f);
                }
            }
            private IEnumerator FadeStorm(GameObject storm)
            {
                foreach (var d in storm.GetComponentsInChildren<DamageHero>(true)) d.enabled = false;
                foreach (var c in storm.GetComponentsInChildren<Collider2D>(true)) c.enabled = false;
                var sprites = storm.GetComponentsInChildren<tk2dSprite>(true);
                var origCol = new Color[sprites.Length];
                for (int i = 0; i < sprites.Length; i++) origCol[i] = sprites[i].color;
                var srs = storm.GetComponentsInChildren<SpriteRenderer>(true);
                var origSr = new Color[srs.Length];
                for (int i = 0; i < srs.Length; i++) origSr[i] = srs[i].color;
                Vector3 origScale = storm.transform.localScale;
                storm.transform.localScale = origScale * 0.4f;
                float t = 0f;
                while (t < FadeInTime)
                {
                    if (storm == null) yield break;
                    t += Time.deltaTime;
                    float k = Mathf.Clamp01(t / FadeInTime);
                    foreach (var c in storm.GetComponentsInChildren<Collider2D>(true)) c.enabled = false;
                    foreach (var d in storm.GetComponentsInChildren<DamageHero>(true)) d.enabled = false;
                    for (int i = 0; i < sprites.Length; i++)
                    {
                        if (sprites[i] == null) continue;
                        var c = origCol[i]; c.a *= k; sprites[i].color = c;
                    }
                    for (int i = 0; i < srs.Length; i++)
                    {
                        if (srs[i] == null) continue;
                        var c = origSr[i]; c.a *= k; srs[i].color = c;
                    }
                    storm.transform.localScale = origScale * (0.4f + 0.6f * k);
                    yield return null;
                }
                if (storm == null) yield break;
                for (int i = 0; i < sprites.Length; i++) if (sprites[i] != null) sprites[i].color = origCol[i];
                for (int i = 0; i < srs.Length; i++) if (srs[i] != null) srs[i].color = origSr[i];
                storm.transform.localScale = origScale;
                foreach (var c in storm.GetComponentsInChildren<Collider2D>(true)) c.enabled = true;
                foreach (var d in storm.GetComponentsInChildren<DamageHero>(true)) d.enabled = true;
                yield return new WaitForSeconds(ExtraLiveTime);
                if (storm != null) Destroy(storm);
                _extraStorms.Remove(storm);
            }
            private IEnumerator Hold(GameObject s)
            {
                _holding = true;
                s.SetActive(true);
                yield return new WaitForSeconds(StormDuration);
                s.SetActive(false);
                _holding = false;
            }
            void OnDestroy()
            {
                if (_sphereG != null) _sphereG.SetActive(false);
                if (_sphereA != null) _sphereA.SetActive(false);
                foreach (var s in _extraStorms) if (s != null) Destroy(s);
                _extraStorms.Clear();
            }
        }
    }
}