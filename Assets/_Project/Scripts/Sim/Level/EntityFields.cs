using KeepyUppy.Sim.Entities;

namespace KeepyUppy.Sim.Level
{
    /// <summary>Every field of <see cref="EntityParams"/>, addressable by id.</summary>
    public enum EntityParamId
    {
        Radius = 0,
        Speed,
        Battery,
        AggroRadius,
        Patience,
        Power,
        Angle,
        Length,
        Delay,
        Duration,
    }

    /// <summary>
    /// Which params each entity type actually uses, and how to read and write one by id.
    ///
    /// The editor's inspector is built from this rather than from a hand-written panel per
    /// hazard. A new type gets its fields listed here and appears in the editor with no UI
    /// code at all — the same reasoning as GDD 5.1, where adding a verb is configuration.
    ///
    /// It lives in Sim/Level, not the editor, because EntityFactory reads the same params.
    /// Two lists that could disagree about what a saw has would be one list too many.
    /// </summary>
    public static class EntityFields
    {
        static readonly EntityParamId[] None = new EntityParamId[0];

        static readonly EntityParamId[] Saw =
        {
            EntityParamId.Radius, EntityParamId.Speed,
        };

        static readonly EntityParamId[] Lamp =
        {
            EntityParamId.Radius, EntityParamId.Length, EntityParamId.Angle,
            EntityParamId.Duration, EntityParamId.Speed,
        };

        static readonly EntityParamId[] Fan =
        {
            EntityParamId.Radius, EntityParamId.Length, EntityParamId.Angle,
            EntityParamId.Power, EntityParamId.Speed,
        };

        /// <summary>The params worth showing for a type. Unknown types show nothing.</summary>
        public static EntityParamId[] For(string type)
        {
            switch (EntityFactory.ParseKind(type))
            {
                case EntityKind.Saw:  return Saw;
                case EntityKind.Lamp: return Lamp;
                case EntityKind.Fan:  return Fan;
                default:              return None;
            }
        }

        /// <summary>
        /// What the type would use if the field were left at 0, so the inspector can show a
        /// real number rather than a lie. See EntityDefaults: 0 means "not set".
        /// </summary>
        public static float DefaultFor(string type, EntityParamId id)
        {
            switch (EntityFactory.ParseKind(type))
            {
                case EntityKind.Saw:
                    if (id == EntityParamId.Radius) return EntityDefaults.SawRadius;
                    if (id == EntityParamId.Speed) return EntityDefaults.SawSpeed;
                    return 0f;

                case EntityKind.Lamp:
                    if (id == EntityParamId.Radius) return EntityDefaults.LampRadius;
                    if (id == EntityParamId.Length) return EntityDefaults.LampLength;
                    if (id == EntityParamId.Angle) return EntityDefaults.LampAngle;
                    if (id == EntityParamId.Duration) return EntityDefaults.LampDuration;
                    return 0f;

                case EntityKind.Fan:
                    if (id == EntityParamId.Radius) return EntityDefaults.FanRadius;
                    if (id == EntityParamId.Length) return EntityDefaults.FanLength;
                    if (id == EntityParamId.Angle) return EntityDefaults.FanAngle;
                    if (id == EntityParamId.Power) return EntityDefaults.FanPower;
                    return 0f;

                default:
                    return 0f;
            }
        }

        /// <summary>Short label, matching the JSON key so the file and the panel agree.</summary>
        public static string Label(EntityParamId id)
        {
            switch (id)
            {
                case EntityParamId.AggroRadius: return "aggro_radius";
                default: return id.ToString().ToLowerInvariant();
            }
        }

        public static float Get(EntityParams p, EntityParamId id)
        {
            if (p == null) return 0f;

            switch (id)
            {
                case EntityParamId.Radius:      return p.radius;
                case EntityParamId.Speed:       return p.speed;
                case EntityParamId.Battery:     return p.battery;
                case EntityParamId.AggroRadius: return p.aggro_radius;
                case EntityParamId.Patience:    return p.patience;
                case EntityParamId.Power:       return p.power;
                case EntityParamId.Angle:       return p.angle;
                case EntityParamId.Length:      return p.length;
                case EntityParamId.Delay:       return p.delay;
                case EntityParamId.Duration:    return p.duration;
                default:                        return 0f;
            }
        }

        public static void Set(EntityParams p, EntityParamId id, float value)
        {
            if (p == null) return;

            switch (id)
            {
                case EntityParamId.Radius:      p.radius = value; break;
                case EntityParamId.Speed:       p.speed = value; break;
                case EntityParamId.Battery:     p.battery = value; break;
                case EntityParamId.AggroRadius: p.aggro_radius = value; break;
                case EntityParamId.Patience:    p.patience = value; break;
                case EntityParamId.Power:       p.power = value; break;
                case EntityParamId.Angle:       p.angle = value; break;
                case EntityParamId.Length:      p.length = value; break;
                case EntityParamId.Delay:       p.delay = value; break;
                case EntityParamId.Duration:    p.duration = value; break;
            }
        }

        /// <summary>path_mode values, in the order the inspector cycles them.</summary>
        public static readonly string[] PathModes = { "pingpong", "loop", "once" };

        /// <summary>start_state values (GDD 9.3).</summary>
        public static readonly string[] StartStates = { "active", "dormant" };
    }
}
