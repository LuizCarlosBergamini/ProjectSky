using System;
using System.Collections.Generic;
using UnityEngine;


[Serializable]
public class DialogOption
{
    public string toDialog;
    public string optionContent;
    public string triggerEvent;
}

[Serializable]
public class DialogItem
{
    public string dialogId;
    [TextArea] public string dialogContent;
    public string nextDialog;
    public string triggerEvent;
    [Tooltip("Em segundos")] public int dialogDuration;
    public List<DialogOption> dialogOptions;
    public Entity_SO dialogEntity;
}

[CreateAssetMenu(menuName = "Dialog/New Dialog")]
public class Dialog_SO : ScriptableObject
{
    public List<DialogItem> dialogs;
}
