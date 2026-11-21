using System.Collections.ObjectModel;
using KqlPad.Infrastructure;
using KqlPad.Models;

namespace KqlPad.ViewModels;

public sealed class CategoryVM(QueryCategory model) : Observable
{
    bool _expanded;
    bool _selected;

    public QueryCategory Model { get; } = model;
    public string Name => Model.Category;
    public ObservableCollection<QueryItemVM> Items { get; } = new();
    public string Header => $"{Model.Category}  ({Items.Count})";

    public bool IsExpanded { get => _expanded; set => Set(ref _expanded, value); }
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
}

public sealed class QueryItemVM(CategoryVM parent, QueryDef model) : Observable
{
    bool _selected;

    public CategoryVM Parent { get; } = parent;
    public QueryDef Model { get; } = model;
    public string Name => Model.Name;
    public string Description => Model.Description;

    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
}

public sealed class ParamVM : Observable
{
    readonly Action<ParamVM> _changed;
    string _value;

    public ParamVM(ParamDef def, string value, Action<ParamVM> changed)
    {
        Def = def;
        _value = value;
        _changed = changed;
    }

    public ParamDef Def { get; }
    public string Name => Def.Name;
    public string Label => string.IsNullOrWhiteSpace(Def.Label) ? Def.Name : Def.Label;
    public string Description => Def.Description;

    public string Value
    {
        get => _value;
        set { if (Set(ref _value, value)) _changed(this); }
    }
}
