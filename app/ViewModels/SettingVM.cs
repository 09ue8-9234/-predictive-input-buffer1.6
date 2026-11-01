using System.Windows.Input;
using TestingApp.Models;

namespace TestingApp.ViewModels;

// One setting as the UI sees it. Works for every kind (on/off, number, text); which control shows is decided by the schema.
public sealed class SettingVM : Obs
{
    readonly MainVM _host;
    object _value;

    public SettingSpec Spec { get; }
    public ICommand ResetCommand { get; }

    public SettingVM(SettingSpec spec, MainVM host)
    {
        Spec = spec;
        _host = host;
        _value = spec.Default;
        ResetCommand = new RelayCommand(_ => Commit(Spec.Default), _ => CanEdit);
        host.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainVM.CanEdit)) Raise(nameof(CanEdit));
        };
    }

    public string Key => Spec.Key;
    public string Title => Spec.Title;
    public string Help => Spec.Help;
    public bool IsBool => Spec.IsBool;
    public bool IsNumber => Spec.IsNumber;
    public bool IsText => Spec.IsText;
    public double Min => Spec.Min;
    public double Max => Spec.Max;
    public double Step => Spec.IsInt ? 1 : (Spec.Max - Spec.Min <= 40 ? 0.5 : 1);
    public bool PerLevel => _host.Schema?.PerLevel.Contains(Spec.Key) == true;
    public bool CanEdit => _host.CanEdit;
    public bool IsModified => !Equals(_value, Spec.Default);
    public object Value => _value;

    public bool BoolValue
    {
        get => _value is true;
        set => Commit(value);
    }

    public double NumValue
    {
        get => _value is double d ? d : 0;
        set => Commit(Math.Round(Math.Round(value / Step) * Step, 3));
    }

    public string NumText
    {
        get => Fmt.Num(NumValue);
        set
        {
            if (Fmt.TryParse(value, out double v)) Commit(Math.Clamp(v, Spec.Min, Spec.Max));
            else RaiseAll();
        }
    }

    public string TextValue
    {
        get => _value as string ?? "";
        set => Commit(value);
    }

    void Commit(object v)
    {
        if (_host.Commit(this, v, out object normalized)) _value = normalized;
        RaiseAll();
    }

    // value came from the file or from the running game: no commit
    public void SetFromHost(object v)
    {
        _value = v;
        RaiseAll();
    }

    void RaiseAll()
    {
        Raise(nameof(BoolValue));
        Raise(nameof(NumValue));
        Raise(nameof(NumText));
        Raise(nameof(TextValue));
        Raise(nameof(IsModified));
        Raise(nameof(PerLevel));
    }
}
