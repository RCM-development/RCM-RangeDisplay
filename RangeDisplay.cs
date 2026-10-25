using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using RCM_GUI;
using UnityEngine;

namespace RCM_RangeDisplay
{
    // Every range a unit has, on the units you hover or select. The game draws ONE circle per unit -
    // whichever range its prefab names in visualizeRangeWhileSelected, and only while selected - so a
    // turret's weapon reach, a support unit's skill range or an artillery shell's splash usually go
    // unseen, and enemies show nothing at all. With the toggle on, hovered and selected units get a
    // tinted copy of the game's own range circle per range: weapon (red), skill (blue), effect radii
    // (orange), sight (grey, off by default).
    [BepInPlugin(Guid, "Range Display", "0.1.0")]
    [BepInDependency(RCMManager.IDENTIFIER, BepInDependency.DependencyFlags.HardDependency)]
    public class RangeDisplay : BaseUnityPlugin
    {
        public const string Guid = "RCM.plugins.rangedisplay";

        static RangeDisplay _instance;
        ConfigEntry<KeyboardShortcut> _toggleKey;
        ConfigEntry<bool> _enabled, _weapon, _skill, _effect, _sight, _enemies;
        RCMModUI _ui;

        enum Kind { Weapon, Skill, Effect1, Effect2, Sight }
        static readonly Color[] Tint =
        {
            new Color(1.00f, 0.25f, 0.20f, 0.85f), // weapon
            new Color(0.25f, 0.65f, 1.00f, 0.85f), // skill
            new Color(1.00f, 0.60f, 0.10f, 0.85f), // effect 1
            new Color(1.00f, 0.80f, 0.30f, 0.85f), // effect 2
            new Color(0.80f, 0.80f, 0.80f, 0.55f), // sight
        };

        // units the player is looking at: hovered (game highlight) and selected
        static readonly HashSet<EntityController> Hovered = new HashSet<EntityController>();
        static readonly HashSet<EntityController> Selected = new HashSet<EntityController>();
        // the circles this mod put on each unit, one per kind
        static readonly Dictionary<EntityController, GameObject[]> Rings = new Dictionary<EntityController, GameObject[]>();
        static bool _loggedMaterial;

        void Awake()
        {
            _instance = this;
            // F5 selects skill buildings, F6 opens the RCM panel, F7/F9 are developer keys
            _toggleKey = Config.Bind("Hotkeys", "ToggleRanges", new KeyboardShortcut(KeyCode.F8),
                "Shows or hides all ranges on hovered and selected units. A single key or a combination (e.g. \"F8 + LeftControl\").");
            _enabled = Config.Bind("Display", "Enabled", true, "Ranges are shown (the hotkey flips this).");
            _weapon = Config.Bind("Display", "WeaponRange", true, "Show weapon range (red).");
            _skill = Config.Bind("Display", "SkillRange", true, "Show skill range (blue), for units with a skill that has a limited range.");
            _effect = Config.Bind("Display", "EffectRadius", true, "Show effect radii (orange): splash, auras, skill areas.");
            _sight = Config.Bind("Display", "SightRange", false, "Show sight range (grey).");
            _enemies = Config.Bind("Display", "EnemiesOnHover", true, "Show ranges of hovered enemy units and buildings too.");
            new Harmony(Guid).PatchAll();
            RCMManager.ConnectMod("Range Display").ContinueWith(t => { _ui = t.Result; BuildUi(); SelfTest(); }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // Once at the menu: copies a stock unit's range circle and tints it, so the log says whether
        // the circle's material takes a colour before anyone looks for one in battle.
        static void SelfTest()
        {
            try
            {
                foreach (string id in new[] { "Tier1Tank", "LaserTank", "SingleCannonTurret" })
                {
                    var prefab = Resources.Load(EntityBalancingStore.PrefabLocation(id)) as GameObject;
                    var template = prefab != null ? prefab.GetComponent<EntityController>()?.baseParameters?.rangeCircle : null;
                    if (template == null) continue;
                    var copy = Instantiate(template);
                    int tinted = 0;
                    var props = new List<string>();
                    foreach (var r in copy.GetComponentsInChildren<Renderer>(true))
                    {
                        var m = r.material;
                        foreach (string p in ColorProperties(m)) { m.SetColor(p, Tint[0]); tinted++; props.Add(r.name + ":" + m.shader.name + ":" + p); }
                    }
                    int lines = copy.GetComponentsInChildren<LineRenderer>(true).Length;
                    DestroyImmediate(copy);
                    RCMManager.Log($"Range Display: self-test on {id}'s circle - {tinted} colour slots tinted, {lines} line renderers ({string.Join(", ", props)})");
                    return;
                }
                RCMManager.Log("Range Display: self-test found no range circle to copy");
            }
            catch (Exception ex) { RCMManager.Log("Range Display: self-test failed (" + ex.Message + ")"); }
        }

        void BuildUi()
        {
            if (_ui == null) return;
            _ui.ClearFields();
            _ui.CreateCheckboxField($"Show ranges [{_toggleKey.Value}]", v => { _enabled.Value = v; Refresh(); }, _enabled.Value);
            _ui.CreateCheckboxField("Weapon (red)", v => { _weapon.Value = v; Refresh(); }, _weapon.Value);
            _ui.CreateCheckboxField("Skill (blue)", v => { _skill.Value = v; Refresh(); }, _skill.Value);
            _ui.CreateCheckboxField("Effect radius (orange)", v => { _effect.Value = v; Refresh(); }, _effect.Value);
            _ui.CreateCheckboxField("Sight (grey)", v => { _sight.Value = v; Refresh(); }, _sight.Value);
            _ui.CreateCheckboxField("Enemies on hover", v => { _enemies.Value = v; Refresh(); }, _enemies.Value);
        }

        void Update()
        {
            if (_toggleKey.Value.IsDown())
            {
                _enabled.Value = !_enabled.Value;
                BuildUi(); // the checkbox follows the key
                RCMManager.Log("Range Display: " + (_enabled.Value ? "on" : "off"));
            }
            Refresh();
        }

        // once a frame: every watched unit gets its rings sized to its current values (rolls, hacks
        // and veterancy change them mid-battle); everything else loses them
        void Refresh()
        {
            Hovered.RemoveWhere(e => e == null);
            Selected.RemoveWhere(e => e == null);
            var watched = new HashSet<EntityController>();
            if (_enabled.Value)
            {
                foreach (var e in Selected) watched.Add(e);
                foreach (var e in Hovered) if (_enemies.Value || e.IsControlledByPlayer) watched.Add(e);
            }
            foreach (var entry in Rings.ToList())
            {
                if (entry.Key == null) { foreach (var ring in entry.Value) if (ring != null) Destroy(ring); Rings.Remove(entry.Key); continue; }
                if (!watched.Contains(entry.Key)) foreach (var ring in entry.Value) if (ring != null) ring.SetActive(false);
            }
            foreach (var e in watched) Draw(e);
        }

        void Draw(EntityController e)
        {
            var template = e.baseParameters != null ? e.baseParameters.rangeCircle : null;
            if (template == null) return;
            if (!Rings.TryGetValue(e, out var rings)) Rings[e] = rings = new GameObject[5];
            Show(e, rings, template, Kind.Weapon, _weapon.Value && e.CanAttack && !e.melee ? e.WeaponRange : 0f);
            Show(e, rings, template, Kind.Skill, _skill.Value && e.hasActiveSkill && !e.SkillRangeIsUnlimited ? e.SkillRange : 0f);
            Show(e, rings, template, Kind.Effect1, _effect.Value ? e.EffectRadius1 : 0f);
            Show(e, rings, template, Kind.Effect2, _effect.Value && Mathf.Abs(e.EffectRadius2 - e.EffectRadius1) > 0.05f ? e.EffectRadius2 : 0f);
            Show(e, rings, template, Kind.Sight, _sight.Value ? e.SightRadius : 0f);
        }

        void Show(EntityController e, GameObject[] rings, GameObject template, Kind kind, float cells)
        {
            int k = (int)kind;
            // tiny values are the game's floors (0.1 radius, 0.25 reach), not real ranges
            if (cells <= 0.3f)
            {
                if (rings[k] != null) rings[k].SetActive(false);
                return;
            }
            if (rings[k] == null) rings[k] = Make(template, Tint[k], kind);
            if (rings[k] == null) return;
            float scale = cells * 10f; // the game scales its circle by range x 10 (EntityController.ShowRangeCircle)
            rings[k].transform.localScale = new Vector3(scale, scale, scale);
            if (!rings[k].activeSelf) rings[k].SetActive(true);
        }

        // a copy of the game's own circle beside it, tinted
        static GameObject Make(GameObject template, Color color, Kind kind)
        {
            try
            {
                var ring = Instantiate(template, template.transform.parent);
                ring.name = "RCM_Range_" + kind;
                ring.transform.localPosition = template.transform.localPosition;
                ring.transform.localRotation = template.transform.localRotation;
                foreach (var r in ring.GetComponentsInChildren<Renderer>(true))
                {
                    var m = r.material; // an instance: the game's shared circle keeps its colour
                    var slots = ColorProperties(m);
                    foreach (string p in slots) m.SetColor(p, color);
                    if (!_loggedMaterial)
                    {
                        _loggedMaterial = true;
                        RCMManager.Log($"Range Display: circle shader {m.shader.name}, colour properties: {string.Join(",", slots)}");
                    }
                }
                foreach (var lr in ring.GetComponentsInChildren<LineRenderer>(true)) { lr.startColor = color; lr.endColor = color; }
                ring.SetActive(false);
                return ring;
            }
            catch (Exception ex) { RCMManager.Log("Range Display: circle copy failed (" + ex.Message + ")"); return null; }
        }

        // every colour property the circle's shader exposes (the game's range shader is custom)
        static List<string> ColorProperties(Material m)
        {
            var result = new List<string>();
            var shader = m.shader;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
                if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color) result.Add(shader.GetPropertyName(i));
            return result;
        }

        static void Forget(EntityController e)
        {
            Hovered.Remove(e);
            Selected.Remove(e);
            if (Rings.TryGetValue(e, out var rings)) { foreach (var r in rings) if (r != null) r.SetActive(false); }
        }

        [HarmonyPatch(typeof(EntityController), nameof(EntityController.Highlight))]
        static class Patch_Highlight { static void Postfix(EntityController __instance) => Hovered.Add(__instance); }

        [HarmonyPatch(typeof(EntityController), nameof(EntityController.Unhighlight))]
        static class Patch_Unhighlight { static void Postfix(EntityController __instance) => Hovered.Remove(__instance); }

        [HarmonyPatch(typeof(EntityController), nameof(EntityController.Select))]
        static class Patch_Select { static void Postfix(EntityController __instance) { if (__instance.IsSelected) Selected.Add(__instance); } }

        [HarmonyPatch(typeof(EntityController), nameof(EntityController.Deselect))]
        static class Patch_Deselect { static void Postfix(EntityController __instance) => Selected.Remove(__instance); }

        // a pooled body comes back as another unit: nothing of its last life stays watched
        [HarmonyPatch(typeof(EntityController), "Init")]
        static class Patch_Init { static void Postfix(EntityController __instance) => Forget(__instance); }
    }
}
