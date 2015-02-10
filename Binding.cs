using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Linq;
using System;
using Component = UnityEngine.Component;

public class Binding : MonoBehaviour
{
    #region Error strings
    private string bindinerError = "Invalid binding on {0}: ";
    private string BINDING_ERROR { get { return string.Format(bindingError, gameObject.name); }
    private string NOT_ASSIGNABLE_FROMTO { get { return BINDING_ERROR + "Properties are not assignable or castable from ({2}){0} to ({3}){1}"; } }
    private string ARGUMENT_NULL { get { return BINDING_ERROR + " Argument {0} is null"; } }
    #endregion
    
    public enum BindingMode
    {
        Invalid,
        OneWayToTarget,
        OneWayToSource,
        TwoWay,
        OneTime
    }

    #region Dinding Data
    private object lastTargetValue, lastSourceValue;
    public string SourceFieldName, TargetFieldName;
    public Component Source, Target;
    public BindingMode Mode;
    #endregion
    
    #region Cached Member Accessor Actions
    private Action<object, object> SetSourceValue = null;
    private Action<object, object> SetTargetValue = null;
    private Func<object, object> GetTargetValue = null;
    private Func<object, object> GetSourceValue = null;
    #endregion

    //Validate Binding data and report any errors
    //Initialize bindings and accesors
    void Start()
    {
        if (Source == null || Target == null)
        {
            Debug.LogError(BINDING_ERROR + (Target == null ? "Target is null" : "Source is Null"));
            Mode = BindingMode.Invalid;
        }
        else
        {
            SetAccessors(Source, SourceFieldName, out SetSourceValue, out GetSourceValue);
            SetAccessors(Target, TargetFieldName, out SetTargetValue, out GetTargetValue);
        }
    }

    //User reflection to identify the . delimited nested class member
    //Drill down into the object until the last member is found
    List<MemberInfo> GetMember(object obj, string field)
    {
        var names = field.Split(new string[] { "." }, StringSplitOptions.RemoveEmptyEntries).ToList();
        List<MemberInfo> currentMembers = new List<MemberInfo>();
        foreach (var name in names)
        {
            if (!currentMembers.Any())
            {
                var objType = obj.GetType();
                var members = objType.GetMember(name);
                currentMembers.Add(members[0]);
            }
            else
            {
                var currentMember = currentMembers.Last();
                switch (currentMember.MemberType)
                {
                    case MemberTypes.Field:
                        {
                            var currentInfo = (currentMember as FieldInfo);
                            currentMember = currentInfo.FieldType.GetMember(name)[0];
                        }
                        break;
                    case MemberTypes.Property:
                        {
                            var currentInfo = (currentMember as PropertyInfo);
                            currentMember = currentInfo.PropertyType.GetMember(name)[0];
                        }
                        break;
                }
                currentMembers.Add(currentMember);
            }
        }
        return currentMembers;
    }

    //Reflect Setter and Getter for the specified property path on the provided object.
    void SetAccessors(object obj, string name, out Action<object, object> setter, out Func<object, object> getter)
    {
        List<Func<object, object>> setterPath = new List<Func<object, object>>();
        List<Func<object, object>> getterPath = new List<Func<object, object>>();
        Action<object, object> valueSetter = null;
        var members = GetMember(obj, name);

        foreach (var member in members)
            switch (member.MemberType)
            {
                case MemberTypes.Field:
                    {
                        var field = member as FieldInfo;
                        if (members.Last().Equals(member))
                            valueSetter = (object ob, object value) =>
                            {
                                if (!field.IsPublic)
                                {
                                    Debug.LogError(string.Format("{0} ({1}){2} is not a public writable field.", BINDING_ERROR, field.FieldType, field.Name));
                                    Mode = BindingMode.Invalid;
                                    return;
                                }
                                var converter = TypeDescriptor.GetConverter(value.GetType());
                                if (converter.CanConvertTo(field.FieldType))
                                    field.SetValue(ob, converter.ConvertTo(value, field.FieldType));
                                else
                                {
                                    Debug.LogError(string.Format("{0} ({1}){2} could not be converted to destination ({3}){4}.", BINDING_ERROR, value.GetType().Name, name, field.FieldType, field.Name));
                                    Mode = BindingMode.Invalid;
                                }
                            };
                        else
                            setterPath.Add((object o) => field.GetValue(o));
                        getterPath.Add((object o) => field.GetValue(o));
                    }
                    break;
                case MemberTypes.Property:
                    {
                        var property = member as PropertyInfo;
                        if (members.Last().Equals(member))
                            valueSetter = (object ob, object value) =>
                            {
                                if (!property.CanWrite)
                                {
                                    Debug.LogError(string.Format("{0} ({1}){2} is a readonly property.", BINDING_ERROR, property.PropertyType, property.Name));
                                    Mode = BindingMode.Invalid;
                                    return;
                                }
                                var converter = TypeDescriptor.GetConverter(value.GetType());
                                if (converter.CanConvertTo(property.PropertyType))
                                    property.SetValue(ob, converter.ConvertTo(value, property.PropertyType), null);
                                else
                                {
                                    Debug.LogError(string.Format("{0} ({1}){2} could not be converted to destination ({3}){4}.", BINDING_ERROR, value.GetType().Name, name, property.PropertyType, property.Name));
                                    Mode = BindingMode.Invalid;
                                }
                            };
                        else
                            setterPath.Add((object o) => property.GetValue(o, null));
                        getterPath.Add(
                        (object o) =>
                                    property.GetValue(o, null)
                        );
                    }
                    break;
                default:
                    Debug.LogError(BINDING_ERROR + "(Source)" + Source.name + "." + SourceFieldName + " field/property was not found");
                    Mode = BindingMode.Invalid;
                    setter = null;
                    getter = null;
                    break;
            }

        getter = o =>
        {
            Func<object, object> lastGetter = getterPath.FirstOrDefault() as Func<object, object>;
            if (lastGetter == null)
                Debug.Log("No getters");
            else if (o == null)
            {
                Debug.Log("Getter: object is null");
            }
            else
            {
                object result = lastGetter.Invoke(o);
                if (result == null)
                    Debug.Log("First getter returns null");
                else
                    for (int i = 1; i < getterPath.Count; i++)
                    {
                        result = getterPath[i].Invoke(result);
                        if (result == null)
                            Debug.Log(string.Format("Getter {0} returns null", i));
                    }
                return result;
            }
            return null;
        };

        setter = (o, value) =>
        {
            object target = o;
            if (target == null) Debug.Log("Setter: Object is null");
            if (setterPath.Any())
            {
                Func<object, object> pathGetter = setterPath.FirstOrDefault();
                if (pathGetter == null)
                    throw new NullReferenceException();

                target = pathGetter.Invoke(target);
                if (target == null)
                    throw new NullReferenceException();

                for (int i = 1; i < setterPath.Count; i++)
                    target = setterPath[i].Invoke(target);
            }

            valueSetter.Invoke(target, value);
        };
    }

    //Check for changes and apply the value update.
    //Force will update the value regardless of if the value has changed.
    void UpdateTarget(bool force = false)
    {
        var sourceValue = GetSourceValue.Invoke(Source);
        bool sourceChanged = sourceValue.Equals(lastSourceValue);
        if (sourceChanged || force)
        {
            SetTargetValue.Invoke(Target, sourceValue);
        }
    }

    //Check for changes and apply the value update.
    //Force will update the value regardless of if the value has changed.
    void UpdateSource()
    {
        var targetValue = GetTargetValue.Invoke(Target);
        bool targetChanged = !targetValue.Equals(lastTargetValue);
        if (targetChanged)
            SetSourceValue.Invoke(Source, targetValue);
    }

    //Evaluate binding per frame
    void Update()
    {
        if (Mode == BindingMode.Invalid || SetSourceValue == null || SetTargetValue == null || GetSourceValue == null || GetTargetValue == null)
            return;
        lastSourceValue = GetSourceValue.Invoke(Source);
        lastTargetValue = GetTargetValue.Invoke(Target);
        switch (Mode)
        {
            case BindingMode.OneTime:
                UpdateTarget(true);
                Mode = BindingMode.Invalid;
                break;
            case BindingMode.OneWayToSource:
                UpdateSource();
                break;
            case BindingMode.OneWayToTarget:
                UpdateTarget();
                break;
            case BindingMode.TwoWay:
                UpdateSource();
                UpdateTarget();
                break;
        }
    }
}
