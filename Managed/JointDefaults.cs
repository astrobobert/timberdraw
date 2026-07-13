using System.Collections.Generic;
using System.Text.Json;

namespace TimberDraw
{
    // Per-JOINT-TYPE user default specs -- the values a joint starts from in the Joints pane AND in the
    // console T* commands (TJoint/TStrut/TBrace/...), replacing the baked-in *Spec.Default when the user
    // has saved one. The user dials in a joint in the Joints pane and hits "Set as default"; that type's
    // spec is persisted here. One default per type (no named library). Mirrors Frame\TypeDefaults.
    //
    // Storage: ONE user-scoped Settings string (JointDefaultsJson) holding a dictionary
    //   key(preset display name, e.g. "Box tenon") -> spec-struct JSON.
    // The specs are plain field-based structs, so serialization needs IncludeFields (the same
    // precedent as the frame-side registry). Every getter is saved-or-factory and NEVER throws: these seed ManagedCommands' static
    // field initializers, where an exception would be a TypeInitializationException killing every command
    // -- any parse/IO failure just falls back to the factory value.
    public static class JointDefaults
    {
        private static Properties.Settings S => Properties.Settings.Default;

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions { IncludeFields = true };

        // Store keys = the ConnectionType preset display names (must match ConnectionType.Name exactly:
        // the pane keys a save off the active preset's Name, and BOM/FromState lookups are by name).
        public const string KeyBox         = "Box tenon";
        public const string KeyStrut       = "Strut tenon";
        public const string KeyBrace       = "Brace tenon";
        public const string KeyRafterFoot  = "Rafter foot";
        public const string KeyRidge       = "Ridge housing";
        public const string KeyRafterHead  = "Rafter head";
        public const string KeyCommonRidge = "Common -> ridge";
        public const string KeyBirdsmouth  = "Birdsmouth";
        public const string KeyPurlin      = "Housed dovetail";   // host-neutral (purlin->rafter, joist->carrier)
        public const string KeyQPRafter    = "QP rafter apex";
        public const string KeyTusk        = "Tusk tenon";        // summer -> girt: soffit-bearing housing + deep tenon

        // Saved-or-factory getters -- the single source every consumer (ConnectionType factories, Spec*
        // mappers, ManagedCommands sticky seeds) reads joint defaults from.
        public static ManagedTimber.JointSpec        Joint       => Get(KeyBox,         ManagedTimber.JointSpec.Default);
        public static ManagedTimber.StrutTenonSpec   Strut       => Get(KeyStrut,       ManagedTimber.StrutTenonSpec.Default);
        public static ManagedTimber.StrutTenonSpec   Brace       => Get(KeyBrace,       ManagedTimber.StrutTenonSpec.BraceDefault);
        public static ManagedTimber.RafterFootSpec   RafterFoot  => Get(KeyRafterFoot,  ManagedTimber.RafterFootSpec.Default);
        public static ManagedTimber.RidgeHousingSpec Ridge       => Get(KeyRidge,       ManagedTimber.RidgeHousingSpec.Default);
        public static ManagedTimber.RafterHeadSpec   RafterHead  => Get(KeyRafterHead,  ManagedTimber.RafterHeadSpec.Default);
        public static ManagedTimber.CommonRidgeSpec  CommonRidge => Get(KeyCommonRidge, ManagedTimber.CommonRidgeSpec.Default);
        public static ManagedTimber.CommonEaveSpec   CommonEave  => Get(KeyBirdsmouth,  ManagedTimber.CommonEaveSpec.Default);
        public static ManagedTimber.PurlinRafterSpec Purlin      => Get(KeyPurlin,      ManagedTimber.PurlinRafterSpec.Default);
        public static ManagedTimber.StrutTenonSpec   QPRafter    => Get(KeyQPRafter,    ManagedTimber.StrutTenonSpec.QPRafterDefault);
        public static ManagedTimber.JointSpec        Tusk        => Get(KeyTusk,        ManagedTimber.JointSpec.TuskDefault);

        // The saved default for `key`, or `factory` when none is saved / the stored JSON doesn't parse.
        public static T Get<T>(string key, T factory) where T : struct
        {
            try
            {
                var map = Load();
                if (!map.TryGetValue(key, out string json) || string.IsNullOrWhiteSpace(json)) return factory;
                return JsonSerializer.Deserialize<T>(json, JsonOpts);
            }
            catch (System.Exception ex)
            {
                // One corrupt saved default silently reverts that joint type to factory values.
                Diag.Warn("JointDefaults.Get", key + " saved default unreadable, using factory: " + ex.Message);
                return factory;
            }
        }

        // Persist `spec` as the user default for `key`, then re-seed the matching console sticky so the
        // T* commands pick it up immediately (putting the push here means no caller can forget it).
        public static void Save<T>(string key, T spec) where T : struct
        {
            var map = Load();
            map[key] = JsonSerializer.Serialize(spec, JsonOpts);
            Persist(map);
            ManagedCommands.ReseedJointSticky(key);
        }

        // Drop the saved default for `key` (back to factory), re-seeding the matching sticky.
        public static void Reset(string key)
        {
            var map = Load();
            if (map.Remove(key)) Persist(map);
            ManagedCommands.ReseedJointSticky(key);
        }

        public static bool HasSaved(string key) => Load().ContainsKey(key);

        private static void Persist(Dictionary<string, string> map)
        {
            try
            {
                S.JointDefaultsJson = JsonSerializer.Serialize(map);
                S.Save();
            }
            catch (System.Exception ex)   // settings write denied (roaming profile) -- in-memory value still applies
            { Diag.Warn("JointDefaults.Persist", "settings write failed (in-memory value still applies): " + ex.Message); }
        }

        private static Dictionary<string, string> Load()
        {
            string json;
            try { json = S.JointDefaultsJson; }
            catch (System.Exception ex)
            {
                Diag.Warn("JointDefaults.Load", "settings read failed: " + ex.Message);
                return new Dictionary<string, string>();
            }
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                       ?? new Dictionary<string, string>();
            }
            catch (System.Exception ex)
            {
                // The whole saved-defaults store is unreadable: every joint type reverts to factory.
                Diag.Warn("JointDefaults.Load", "saved defaults corrupt, starting empty: " + ex.Message);
                return new Dictionary<string, string>();
            }
        }
    }
}
