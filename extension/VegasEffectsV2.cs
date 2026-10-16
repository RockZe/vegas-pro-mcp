using System;
using System.Collections.Generic;
using ScriptPortal.Vegas;

namespace UnitedTaps.VegasMcp
{
    internal static partial class VegasActions
    {
        private sealed class EffectTarget
        {
            public string Scope;
            public string Label;
            public Effects Effects;
            public PlugInNode PluginRoot;
        }

        private static object ListPresets(Vegas vegas, Dictionary<string, object> p)
        {
            string kind = GetString(p, "kind", "video").ToLowerInvariant();
            PlugInNode root;
            if (kind == "audio") root = vegas.AudioFX;
            else if (kind == "video") root = vegas.VideoFX;
            else if (kind == "transition") root = vegas.Transitions;
            else if (kind == "generator") root = vegas.Generators;
            else throw new InvalidOperationException("kind must be audio, video, transition, or generator.");
            string pluginName = RequireString(p, "plugin_name");
            PlugInNode node = FindPlugin(root, pluginName);
            if (node == null) throw new InvalidOperationException("Plug-in was not found: " + pluginName);
            List<object> presets = new List<object>();
            foreach (EffectPreset preset in node.Presets)
                presets.Add(new Dictionary<string, object> { { "index", preset.Index }, { "name", preset.Name } });
            List<object> quickPresets = new List<object>();
            for (int i = 0; i < node.QuickPresetCount; i++) quickPresets.Add(new Dictionary<string, object> { { "index", i }, { "name", node.QuickPresetName(i) } });
            return new Dictionary<string, object> { { "kind", kind }, { "plugin", node.Name }, { "plugin_id", node.UniqueID }, { "presets", presets }, { "quick_presets", quickPresets } };
        }

        private static object ListTrackEffects(Vegas vegas, Dictionary<string, object> p)
        {
            GuardProject(vegas.Project, p);
            Dictionary<string, object> targetParams = CloneDictionary(p);
            targetParams["scope"] = "track";
            EffectTarget target = ResolveEffectTarget(vegas, targetParams, false);
            return EffectTargetData(target);
        }

        private static object AddTrackEffect(Vegas vegas, Dictionary<string, object> p)
        {
            Dictionary<string, object> targetParams = CloneDictionary(p);
            targetParams["scope"] = "track";
            return AddTargetEffect(vegas, targetParams);
        }

        private static object RemoveTrackEffect(Vegas vegas, Dictionary<string, object> p)
        {
            Dictionary<string, object> targetParams = CloneDictionary(p);
            targetParams["scope"] = "track";
            return RemoveTargetEffect(vegas, targetParams);
        }

        private static object ListBusEffects(Vegas vegas, Dictionary<string, object> p)
        {
            GuardProject(vegas.Project, p);
            Dictionary<string, object> targetParams = CloneDictionary(p);
            if (!Has(targetParams, "scope")) targetParams["scope"] = "bus";
            EffectTarget target = ResolveEffectTarget(vegas, targetParams, false);
            return EffectTargetData(target);
        }

        private static object AddBusEffect(Vegas vegas, Dictionary<string, object> p)
        {
            Dictionary<string, object> targetParams = CloneDictionary(p);
            if (!Has(targetParams, "scope")) targetParams["scope"] = "bus";
            return AddTargetEffect(vegas, targetParams);
        }

        private static object RemoveBusEffect(Vegas vegas, Dictionary<string, object> p)
        {
            Dictionary<string, object> targetParams = CloneDictionary(p);
            if (!Has(targetParams, "scope")) targetParams["scope"] = "bus";
            return RemoveTargetEffect(vegas, targetParams);
        }

        private static object AddTargetEffect(Vegas vegas, Dictionary<string, object> p)
        {
            EffectTarget target = ResolveEffectTarget(vegas, p, true);
            string pluginName = RequireString(p, "plugin_name");
            PlugInNode node = FindPlugin(target.PluginRoot, pluginName);
            if (node == null) throw new InvalidOperationException("Effect plug-in was not found for the selected target: " + pluginName);
            Effect effect = target.Effects.AddEffect(node);
            if (Has(p, "preset")) effect.Preset = RequireString(p, "preset");
            if (Has(p, "bypass")) effect.Bypass = RequireBool(p, "bypass");
            Dictionary<string, object> result = EffectTargetData(target);
            result["effect_index"] = effect.Index;
            return result;
        }

        private static object RemoveTargetEffect(Vegas vegas, Dictionary<string, object> p)
        {
            EffectTarget target = ResolveEffectTarget(vegas, p, true);
            int index = RequireInt(p, "effect_index");
            Effect effect = GetEffect(target.Effects, index);
            string description = effect.Description;
            target.Effects.RemoveAt(index);
            Dictionary<string, object> result = EffectTargetData(target);
            result["removed_effect"] = description;
            return result;
        }

        private static object SetEffectState(Vegas vegas, Dictionary<string, object> p)
        {
            EffectTarget target = ResolveEffectTarget(vegas, p, true);
            Effect effect = GetEffect(target.Effects, RequireInt(p, "effect_index"));
            if (Has(p, "bypass")) effect.Bypass = RequireBool(p, "bypass");
            if (Has(p, "preset")) effect.Preset = RequireString(p, "preset");
            if (Has(p, "apply_before_pan_crop")) effect.ApplyBeforePanCrop = RequireBool(p, "apply_before_pan_crop");
            if (Has(p, "apply_after_composite")) effect.ApplyAfterComposite = RequireBool(p, "apply_after_composite");
            return EffectTargetData(target);
        }

        private static object SetTargetOfxParameter(Vegas vegas, Dictionary<string, object> p)
        {
            EffectTarget target = ResolveEffectTarget(vegas, p, true);
            Effect effect = GetEffect(target.Effects, RequireInt(p, "effect_index"));
            if (!effect.IsOFX) throw new InvalidOperationException("The selected effect is not OFX.");
            string parameterName = RequireString(p, "parameter_name");
            OFXParameter parameter = effect.OFXEffect.FindParameterByName(parameterName);
            if (parameter == null) throw new InvalidOperationException("OFX parameter was not found: " + parameterName);
            Timecode time = HasAnyTime(p, "at") ? RequireTime(vegas.Project, p, "at", false) : null;
            ApplyOfxParameterValue(parameter, Require(p, "value"), time);
            parameter.ParameterChanged();
            Dictionary<string, object> result = EffectTargetData(target);
            result["parameter"] = ParameterData(parameter);
            return result;
        }

        private static object CopyEffectChain(Vegas vegas, Dictionary<string, object> p)
        {
            Dictionary<string, object> sourceParams = Require(p, "source") as Dictionary<string, object>;
            Dictionary<string, object> targetParams = Require(p, "target") as Dictionary<string, object>;
            if (sourceParams == null || targetParams == null) throw new InvalidOperationException("source and target must be effect-target objects.");
            EffectTarget source = ResolveEffectTarget(vegas, sourceParams, true);
            EffectTarget target = ResolveEffectTarget(vegas, targetParams, true);
            if (Object.ReferenceEquals(source.Effects, target.Effects)) throw new InvalidOperationException("Source and target effect chains are the same.");
            bool copyParameters = GetBool(p, "copy_parameters", true);
            bool allowFlatten = GetBool(p, "allow_flatten_animated", false);
            if (copyParameters && !allowFlatten)
            {
                foreach (Effect sourceEffect in source.Effects)
                {
                    if (!sourceEffect.IsOFX) continue;
                    foreach (OFXParameter sourceParameter in sourceEffect.OFXEffect.Parameters)
                    {
                        if (!sourceParameter.Secret && sourceParameter.Persisted && sourceParameter.IsAnimated)
                            throw new InvalidOperationException("Animated OFX parameter requires allow_flatten_animated=true or dedicated keyframe copying: " + sourceParameter.Name);
                    }
                }
            }
            if (GetBool(p, "clear_target", false))
            {
                if (!GetBool(p, "allow_clear_target", false)) throw new InvalidOperationException("Clearing the target chain requires allow_clear_target=true.");
                target.Effects.Clear();
            }
            List<object> copied = new List<object>();
            foreach (Effect sourceEffect in source.Effects)
            {
                Effect destination = target.Effects.AddEffect(sourceEffect.PlugIn);
                if (sourceEffect.CurrentPreset != null && !String.IsNullOrWhiteSpace(sourceEffect.CurrentPreset.Name)) destination.Preset = sourceEffect.CurrentPreset.Name;
                destination.Bypass = sourceEffect.Bypass;
                if (sourceEffect.PlugIn != null && sourceEffect.PlugIn.IsVideo)
                {
                    destination.ApplyBeforePanCrop = sourceEffect.ApplyBeforePanCrop;
                    destination.ApplyAfterComposite = sourceEffect.ApplyAfterComposite;
                }
                int parameterCount = 0;
                if (copyParameters && sourceEffect.IsOFX && destination.IsOFX)
                {
                    foreach (OFXParameter sourceParameter in sourceEffect.OFXEffect.Parameters)
                    {
                        if (sourceParameter.Secret || !sourceParameter.Persisted) continue;
                        OFXParameter destinationParameter = destination.OFXEffect.FindParameterByName(sourceParameter.Name);
                        if (destinationParameter == null || destinationParameter.ParameterType != sourceParameter.ParameterType) continue;
                        object value = ReadOfxValue(sourceParameter);
                        if (value == null) continue;
                        ApplyOfxParameterValue(destinationParameter, value, null);
                        destinationParameter.ParameterChanged();
                        parameterCount++;
                    }
                    destination.OFXEffect.AllParametersChanged();
                }
                copied.Add(new Dictionary<string, object> { { "source_index", sourceEffect.Index }, { "target_index", destination.Index }, { "plugin", sourceEffect.Description }, { "copied_parameter_count", parameterCount }, { "animated_parameters_flattened", allowFlatten } });
            }
            Dictionary<string, object> result = EffectTargetData(target);
            result["source"] = source.Label;
            result["copied"] = copied;
            return result;
        }

        private static EffectTarget ResolveEffectTarget(Vegas vegas, Dictionary<string, object> p, bool guarded)
        {
            string scope = GetString(p, "scope", Has(p, "event_id") ? "event" : "track").ToLowerInvariant();
            EffectTarget result = new EffectTarget();
            result.Scope = scope;
            if (scope == "event")
            {
                TrackEvent ev = guarded ? GuardEvent(vegas.Project, p) : FindEvent(vegas.Project, RequireLong(p, "event_id"));
                result.Label = "event:" + ev.EventID;
                result.Effects = GetEffects(ev);
                result.PluginRoot = ev.IsAudio() ? vegas.AudioFX : vegas.VideoFX;
            }
            else if (scope == "track")
            {
                Track track = GetTrack(vegas.Project, RequireInt(p, "track_index"));
                GuardTrackName(track, p);
                result.Label = "track:" + track.Index + ":" + track.Name;
                result.Effects = track.Effects;
                result.PluginRoot = track.IsAudio() ? vegas.AudioFX : vegas.VideoFX;
            }
            else if (scope == "master_audio" || scope == "master" || scope == "bus")
            {
                BusTrack bus = ResolveBus(vegas.Project, p);
                result.Label = bus.IsMaster() ? "master_audio_bus" : "bus:" + bus.Index + ":" + bus.Description;
                result.Effects = bus.Effects;
                result.PluginRoot = vegas.AudioFX;
            }
            else if (scope == "video_bus")
            {
                result.Label = "video_bus";
                result.Effects = vegas.Project.VideoBus.Effects;
                result.PluginRoot = vegas.VideoFX;
            }
            else if (scope == "media")
            {
                Media media = ResolveMedia(vegas.Project, p);
                result.Label = "media:" + media.MediaID + ":" + media.FilePath;
                result.Effects = media.Effects;
                result.PluginRoot = media.HasVideo() ? vegas.VideoFX : vegas.AudioFX;
            }
            else throw new InvalidOperationException("Unsupported effect target scope: " + scope);
            return result;
        }

        private static Dictionary<string, object> EffectTargetData(EffectTarget target)
        {
            Dictionary<string, object> result = new Dictionary<string, object>();
            result["scope"] = target.Scope;
            result["target"] = target.Label;
            result["effects"] = EffectData(target.Effects);
            return result;
        }

        private static Effect GetEffect(Effects effects, int index)
        {
            if (index < 0 || index >= effects.Count) throw new InvalidOperationException("effect_index is outside the effect chain.");
            return effects[index];
        }

        private static void ApplyOfxParameterValue(OFXParameter parameter, object raw, Timecode time)
        {
            bool atTime = time != null;
            OFXDoubleParameter d = parameter as OFXDoubleParameter;
            OFXIntegerParameter i = parameter as OFXIntegerParameter;
            OFXBooleanParameter b = parameter as OFXBooleanParameter;
            OFXStringParameter s = parameter as OFXStringParameter;
            OFXChoiceParameter c = parameter as OFXChoiceParameter;
            OFXDouble2DParameter d2 = parameter as OFXDouble2DParameter;
            OFXDouble3DParameter d3 = parameter as OFXDouble3DParameter;
            OFXInteger2DParameter i2 = parameter as OFXInteger2DParameter;
            OFXInteger3DParameter i3 = parameter as OFXInteger3DParameter;
            OFXRGBParameter rgb = parameter as OFXRGBParameter;
            OFXRGBAParameter rgba = parameter as OFXRGBAParameter;
            OFXCustomParameter custom = parameter as OFXCustomParameter;
            if (d != null)
            {
                double value = Convert.ToDouble(raw);
                if (atTime) { d.IsAnimated = true; d.SetValueAtTime(time, value); } else d.Value = value;
            }
            else if (i != null)
            {
                int value = Convert.ToInt32(raw);
                if (atTime) { i.IsAnimated = true; i.SetValueAtTime(time, value); } else i.Value = value;
            }
            else if (b != null)
            {
                bool value = Convert.ToBoolean(raw);
                if (atTime) { b.IsAnimated = true; b.SetValueAtTime(time, value); } else b.Value = value;
            }
            else if (s != null)
            {
                string value = Convert.ToString(raw);
                if (atTime) { s.IsAnimated = true; s.SetValueAtTime(time, value); } else s.Value = value;
            }
            else if (c != null)
            {
                OFXChoice value = FindChoice(c, raw);
                if (atTime) { c.IsAnimated = true; c.SetValueAtTime(time, value); } else c.Value = value;
            }
            else if (d2 != null)
            {
                OFXDouble2D value = ReadDouble2D(raw);
                if (atTime) { d2.IsAnimated = true; d2.SetValueAtTime(time, value); } else d2.Value = value;
            }
            else if (d3 != null)
            {
                OFXDouble3D value = ReadDouble3D(raw);
                if (atTime) { d3.IsAnimated = true; d3.SetValueAtTime(time, value); } else d3.Value = value;
            }
            else if (i2 != null)
            {
                OFXInteger2D value = ReadInteger2D(raw);
                if (atTime) { i2.IsAnimated = true; i2.SetValueAtTime(time, value); } else i2.Value = value;
            }
            else if (i3 != null)
            {
                OFXInteger3D value = ReadInteger3D(raw);
                if (atTime) { i3.IsAnimated = true; i3.SetValueAtTime(time, value); } else i3.Value = value;
            }
            else if (rgb != null)
            {
                OFXColor value = ReadColor(raw, false);
                if (atTime) { rgb.IsAnimated = true; rgb.SetValueAtTime(time, value); } else rgb.Value = value;
            }
            else if (rgba != null)
            {
                OFXColor value = ReadColor(raw, true);
                if (atTime) { rgba.IsAnimated = true; rgba.SetValueAtTime(time, value); } else rgba.Value = value;
            }
            else if (custom != null)
            {
                string value = Convert.ToString(raw);
                if (atTime) { custom.IsAnimated = true; custom.SetValueAtTime(time, value); } else custom.Value = value;
            }
            else throw new InvalidOperationException("This OFX parameter type is not supported: " + parameter.ParameterType);
        }

        private static object ReadOfxValue(OFXParameter parameter)
        {
            OFXDoubleParameter d = parameter as OFXDoubleParameter; if (d != null) return d.Value;
            OFXIntegerParameter i = parameter as OFXIntegerParameter; if (i != null) return i.Value;
            OFXBooleanParameter b = parameter as OFXBooleanParameter; if (b != null) return b.Value;
            OFXStringParameter s = parameter as OFXStringParameter; if (s != null) return s.Value;
            OFXChoiceParameter c = parameter as OFXChoiceParameter; if (c != null) return c.Value == null ? null : (object)c.Value.Name;
            OFXDouble2DParameter d2 = parameter as OFXDouble2DParameter; if (d2 != null) return VectorData(d2.Value.X, d2.Value.Y, null);
            OFXDouble3DParameter d3 = parameter as OFXDouble3DParameter; if (d3 != null) return VectorData(d3.Value.X, d3.Value.Y, d3.Value.Z);
            OFXInteger2DParameter i2 = parameter as OFXInteger2DParameter; if (i2 != null) return VectorData(i2.Value.X, i2.Value.Y, null);
            OFXInteger3DParameter i3 = parameter as OFXInteger3DParameter; if (i3 != null) return VectorData(i3.Value.X, i3.Value.Y, i3.Value.Z);
            OFXRGBParameter rgb = parameter as OFXRGBParameter; if (rgb != null) return ColorData(rgb.Value);
            OFXRGBAParameter rgba = parameter as OFXRGBAParameter; if (rgba != null) return ColorData(rgba.Value);
            OFXCustomParameter custom = parameter as OFXCustomParameter; if (custom != null) return custom.Value;
            return null;
        }

        private static OFXChoice FindChoice(OFXChoiceParameter parameter, object raw)
        {
            foreach (OFXChoice choice in parameter.Choices)
                if (String.Equals(choice.Name, Convert.ToString(raw), StringComparison.OrdinalIgnoreCase) || choice.Index == ConvertToIntOrMinusOne(raw)) return choice;
            throw new InvalidOperationException("OFX choice was not found: " + raw);
        }

        private static Dictionary<string, object> AsObject(object raw, string name)
        {
            Dictionary<string, object> value = raw as Dictionary<string, object>;
            if (value == null) throw new InvalidOperationException(name + " value must be an object.");
            return value;
        }

        private static OFXDouble2D ReadDouble2D(object raw)
        {
            Dictionary<string, object> data = AsObject(raw, "Double2D");
            OFXDouble2D value = new OFXDouble2D(); value.X = RequireDouble(data, "x"); value.Y = RequireDouble(data, "y"); return value;
        }

        private static OFXDouble3D ReadDouble3D(object raw)
        {
            Dictionary<string, object> data = AsObject(raw, "Double3D");
            OFXDouble3D value = new OFXDouble3D(); value.X = RequireDouble(data, "x"); value.Y = RequireDouble(data, "y"); value.Z = RequireDouble(data, "z"); return value;
        }

        private static OFXInteger2D ReadInteger2D(object raw)
        {
            Dictionary<string, object> data = AsObject(raw, "Integer2D");
            OFXInteger2D value = new OFXInteger2D(); value.X = RequireInt(data, "x"); value.Y = RequireInt(data, "y"); return value;
        }

        private static OFXInteger3D ReadInteger3D(object raw)
        {
            Dictionary<string, object> data = AsObject(raw, "Integer3D");
            OFXInteger3D value = new OFXInteger3D(); value.X = RequireInt(data, "x"); value.Y = RequireInt(data, "y"); value.Z = RequireInt(data, "z"); return value;
        }

        private static OFXColor ReadColor(object raw, bool includeAlpha)
        {
            Dictionary<string, object> data = AsObject(raw, includeAlpha ? "RGBA" : "RGB");
            double red = RequireDouble(data, "r"); double green = RequireDouble(data, "g"); double blue = RequireDouble(data, "b");
            return includeAlpha ? new OFXColor(red, green, blue, GetDouble(data, "a", 1)) : new OFXColor(red, green, blue);
        }

        private static Dictionary<string, object> VectorData(object x, object y, object z)
        {
            Dictionary<string, object> data = new Dictionary<string, object>(); data["x"] = x; data["y"] = y; if (z != null) data["z"] = z; return data;
        }

        private static Dictionary<string, object> ColorData(OFXColor color)
        {
            return new Dictionary<string, object> { { "r", color.R }, { "g", color.G }, { "b", color.B }, { "a", color.A } };
        }

        private static Dictionary<string, object> CloneDictionary(Dictionary<string, object> source)
        {
            return new Dictionary<string, object>(source, StringComparer.OrdinalIgnoreCase);
        }
    }
}
