using KeepyUppy.Sim.Entities;
using UnityEngine;

namespace KeepyUppy.Sim.Level
{
    /// <summary>
    /// Turns schema entities into runtime ones (level-editor-spec 3 to GDD 9).
    ///
    /// The only place a type string becomes behaviour. An unrecognised type still produces a
    /// SimEntity — an inert one — so the level round-trips intact and the editor can tell the
    /// author their file is newer than their build.
    /// </summary>
    public static class EntityFactory
    {
        public static EntityKind ParseKind(string type)
        {
            switch (type)
            {
                case "saw":  return EntityKind.Saw;
                case "lamp": return EntityKind.Lamp;
                case "fan":  return EntityKind.Fan;
                default:     return EntityKind.Unknown;
            }
        }

        public static string TypeName(EntityKind kind)
        {
            switch (kind)
            {
                case EntityKind.Saw:  return "saw";
                case EntityKind.Lamp: return "lamp";
                case EntityKind.Fan:  return "fan";
                default:              return "";
            }
        }

        public static PathMode ParseMode(string mode)
        {
            switch (mode)
            {
                case "loop": return PathMode.Loop;
                case "once": return PathMode.Once;
                default:     return PathMode.PingPong;
            }
        }

        public static SimEntity Create(LevelEntity data)
        {
            if (data == null) return null;

            var origin = new Vector2(
                data.pos != null && data.pos.Length > 0 ? data.pos[0] : 0f,
                data.pos != null && data.pos.Length > 1 ? data.pos[1] : 0f);

            EntityParams p = data.@params ?? new EntityParams();

            var entity = new SimEntity
            {
                Id = data.id,
                Kind = ParseKind(data.type),
                Points = EntityPath.FromFlat(origin, data.path),
                Mode = ParseMode(data.path_mode),
                Phase = data.phase,
                Active = data.start_state != "dormant",
            };

            // 0 means "not set", because JsonUtility writes every field of EntityParams
            // whether the author touched it or not. See EntityDefaults.
            switch (entity.Kind)
            {
                case EntityKind.Saw:
                    entity.Radius = EntityDefaults.Or(p.radius, EntityDefaults.SawRadius);
                    entity.Speed = EntityDefaults.Or(p.speed, EntityDefaults.SawSpeed);
                    break;

                case EntityKind.Lamp:
                    entity.Radius = EntityDefaults.Or(p.radius, EntityDefaults.LampRadius);
                    entity.Length = EntityDefaults.Or(p.length, EntityDefaults.LampLength);
                    entity.Angle = p.angle != 0f ? p.angle : EntityDefaults.LampAngle;
                    entity.Duration = EntityDefaults.Or(p.duration, EntityDefaults.LampDuration);
                    entity.Speed = p.speed;
                    break;

                case EntityKind.Fan:
                    entity.Radius = EntityDefaults.Or(p.radius, EntityDefaults.FanRadius);
                    entity.Length = EntityDefaults.Or(p.length, EntityDefaults.FanLength);
                    entity.Angle = p.angle != 0f ? p.angle : EntityDefaults.FanAngle;
                    entity.Power = EntityDefaults.Or(p.power, EntityDefaults.FanPower);
                    entity.Speed = p.speed;
                    break;
            }

            entity.Reset();
            return entity;
        }
    }
}
