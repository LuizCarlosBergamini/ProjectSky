using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-shot tool: writes the lines of the "Roteiro de falas" into the dialog assets, creates the new ones
/// (Mecanico per boss, Daisy's repeat lines in the Hub) and wires them to the Hub Daisy and the vendor.
/// Overwrites the text of every dialog listed here, so delete this file once it has been applied.
/// </summary>
public static class RoteiroDialogBuilder
{
    private const string DialogFolder = "Assets/ObjectData/Dialog";
    private const string HubScenePath = "Assets/Scenes/Hub.unity";
    private const string VendorPrefabPath = "Assets/Renan/Prefabs/UpgradeVendor.prefab";

    private const string D = "Daisy";
    private const string L = "Lucy";
    private const string W = "Wallace";
    private const string M = "Mecanico";

    private readonly struct Line
    {
        public readonly string id, entity, text, trigger;

        public Line(string id, string entity, string text, string trigger = "")
        {
            this.id = id;
            this.entity = entity;
            this.text = text;
            this.trigger = trigger;
        }
    }

    [MenuItem("Tools/Lucy/Aplicar Roteiro de Falas")]
    public static void Apply()
    {
        // HUB - INICIO
        Write("Tutorial",
            new Line("daisy-inicio-01", D, "Ei, você!"),
            new Line("lucy-inicio-01", L, "... Quem é você? Onde estou?"),
            new Line("daisy-inicio-02", D, "Eu sou a Daisy Macabell, e esse é o Wallace Macabell, meu irmão. Te encontramos caída aqui e queremos te ajudar a consertar suas peças."),
            new Line("daisy-inicio-02b", D, "Bom, eu vou te ajudar; o Wallace provavelmente vai continuar escrevendo sua poesia estranha..."),
            new Line("wallace-inicio-01", W, "Ei, pelo menos não sujo minhas mãos de graxa!"),
            new Line("daisy-inicio-03", D, "Mas... Qual é o seu nome? Como veio parar aqui?"),
            new Line("lucy-inicio-02", L, "Não me lembro muito bem de nada... está tudo tão confuso... acho que me chamo... Lucy! Lembro vagamente de um homem me dando esse nome..."),
            new Line("daisy-inicio-04", D, "Bom, Lucy, nós vamos te ajudar. Para isso, deixe-me explicar como as coisas funcionam aqui."),
            new Line("daisy-inicio-05", D, "Você pode se mover para os lados usando 'A' e 'D'; para pular, use 'W' ou 'espaço'. Quando encontrar inimigos, use o 'botão esquerdo do mouse' para atacar."),
            new Line("daisy-inicio-06", D, "Ah! Além disso, para usar seu agarrador e pendurar-se em blocos de apoio, segure o 'botão direito do mouse'; o 'espaço' pode te dar um impulso na hora de soltar."),
            new Line("wallace-inicio-02", W, "Do outro lado da passagem à frente, você encontrará inimigos desafiadores. Fique firme; a Daisy estará lá para te ajudar."),
            new Line("daisy-inicio-07", D, "Agora é com você. Cruze a passagem usando o agarrador. Te encontro do outro lado. Boa sorte!", "tutorial-iniciado"));

        // PRE-ARENA - MEDUSA
        Write("Missao-boss",
            new Line("daisy-boss-01", D, "Que bom! Você conseguiu."),
            new Line("daisy-boss-02", D, "Logo à frente você encontrará a Medusa."),
            new Line("lucy-boss-01", L, "Medusa? Como assim?!"),
            new Line("daisy-boss-03", D, "Ela protege a arena... Derrotando-a, ganhará uma recompensa e, ao voltar do combate, podemos começar os consertos!"),
            new Line("daisy-boss-04", D, "Wallace pediu para te avisar: tome cuidado com o ataque petrificante da Medusa... Eu tenho muito medo de tudo isso... Te espero do outro lado!", "missao-boss-iniciada"));
        Write("Missao-boss-lembrete",
            new Line("daisy-bosslembrete-01", D, "A Medusa está logo à frente. Te espero do outro lado!"));

        // POS-ARENA - MEDUSA
        Write("Boss-derrotado-medusa",
            new Line("daisy-medusaderrota-01", D, "Parabéns, Lucy! Você conseguiu! Lembrei de uma coisa agora... Depois de derrotá-los, os inimigos voltam para as arenas; assim, você pode conseguir mais recompensas para acelerar os consertos!"),
            new Line("lucy-medusaderrota-01", L, "Então, se o próximo for difícil demais..."),
            new Line("daisy-medusaderrota-02", D, "Você volta, junta as peças e o mecânico te ajuda com melhorias!"));

        // HUB - POS-MEDUSA
        Write("Missao-boss-finalizada",
            new Line("daisy-bossfim-01", D, "Parabéns por derrotar a Medusa! Espero que a dica do Wallace tenha te ajudado."),
            new Line("lucy-bossfim-01", L, "Não foi fácil, mas ela não vai mais bloquear o caminho. Obrigada pelo aviso, Wallace!"),
            new Line("wallace-bossfim-01", W, "Ei, pode contar comigo, Lucy, só não ponho a mão na graxa, haha."),
            new Line("daisy-bossfim-02", D, "Inclusive, se você ainda não falou com o mecânico, dê uma passadinha lá. Ele fará os reparos e as melhorias nas suas peças. Já o avisei sobre você."));
        Dialog_SO hubKeepGoing = Write("Daisy-hub-prosseguir",
            new Line("daisy-prosseguir-01", D, "Você deve prosseguir, Lucy. Há mais inimigos à frente bloqueando o caminho e mais recompensas e consertos pela frente. Seja forte!"));
        Dialog_SO vendorMedusa = Write("Mecanico-pos-medusa",
            new Line("mecanico-medusa-01", M, "Ei! Você deve ser a Lucy. A Daisy me avisou sobre você. A Medusa deve ter te recompensado com uma \"Engrenagem de Bronze\"."),
            new Line("mecanico-medusa-02", M, "Usando-a, você pode fazer um reparo na loja de melhorias. Escolha com cuidado; a quantidade de materiais aumenta de acordo com o nível."));

        // PRE-ARENA - KITSUNE
        Write("Missao-boss-2",
            new Line("daisy-boss2-01", D, "Lucy, seja cuidadosa, a arena à frente é defendida por uma raposa mitológica de nove caudas... a Kitsune."),
            new Line("lucy-boss2-01", L, "Depois da Medusa, uma raposa vai ser moleza!"),
            new Line("daisy-boss2-02", D, "Não se engane! Wallace já me avisou: esse monstro lança fogo-fátuo à distância e, quando se enfurece, solta rajadas de chama por todos os lados! Além disso, suas caudas flamejantes incendeiam o chão à sua volta."),
            new Line("daisy-boss2-03", D, "Tome cuidado! Lembre-se, ao vencer, ganhará uma recompensa e poderemos continuar os consertos.", "missao-boss-2-iniciada"));
        Write("Missao-boss-2-lembrete",
            new Line("daisy-boss2lembrete-01", D, "A Kitsune está logo à frente. Cuidado com as chamas, não fique parada. Te espero do outro lado!"));

        // POS-ARENA - KITSUNE
        Write("Boss-derrotado-kitsune",
            new Line("daisy-kitsunederrota-01", D, "Parabéns, Lucy! Você conseguiu. Derrotou a Kitsune. Ainda vejo a fumaça..."),
            new Line("lucy-kitsunederrota-01", L, "Foi por pouco... Parece que fica cada vez mais difícil..."),
            new Line("daisy-kitsunederrota-02", D, "O caminho não é fácil, mas lembre-se: a Medusa e a Kitsune retornam para suas arenas. Se precisar de mais melhorias antes de prosseguir, um passo pra trás pode significar dois para frente."));

        // HUB - POS-KITSUNE
        Write("Missao-boss-2-finalizada",
            new Line("daisy-boss2fim-01", D, "Parabéns por derrotar a Kitsune! Nem a nove caudas pôde te parar!"),
            new Line("lucy-boss2fim-01", L, "Se não fossem as dicas do Wallace e seu suporte, não conseguiria; aquele fogo-fátuo não era brincadeira!"),
            new Line("daisy-boss2fim-02", D, "Estaremos sempre na sua retaguarda. Agora vá ao mecânico para que ele faça os consertos e te dê melhorias!"));
        Dialog_SO vendorKitsune = Write("Mecanico-pos-kitsune",
            new Line("mecanico-kitsune-01", M, "Essa \"Engrenagem de Ouro\" você só pode ter pegado da Kitsune... Parabéns por derrotá-la. Vamos escolher qual melhoria será hoje?"));

        // PRE-ARENA - MAGO
        Write("Missao-boss-3",
            new Line("daisy-boss3-01", D, "Lucy, espera! Há uma magia muito forte na arena à frente..."),
            new Line("lucy-boss3-01", L, "Mais um inimigo? Depois da Medusa e de Kitsune, já estou pronta para qualquer coisa."),
            new Line("daisy-boss3-02", D, "Dessa vez é diferente... Wallace diz que é provável que o guardião desta magia tenha invocado os outros inimigos. Assim, ele lembrou-se do Mago que defende esse lugar."),
            new Line("daisy-boss3-03", D, "O Mago ataca de longe, com flechas e esferas de magia. Ele não gosta de apanhar: depois de alguns golpes, recua de repente! Não deixe-o escapar."),
            new Line("daisy-boss3-04", D, "Derrote-o para receber a recompensa e avançar, mas seja muito cuidadosa. Te espero do outro lado.", "missao-boss-3-iniciada"));
        Write("Missao-boss-3-lembrete",
            new Line("daisy-boss3lembrete-01", D, "O mago está logo à frente. Tome cuidado. Te espero do outro lado."));

        // HUB - POS-MAGO
        Write("Missao-boss-3-finalizada",
            new Line("daisy-boss3fim-01", D, "Você conseguiu?! Derrotou o mago e livrou todos nós dos perigos de suas invocações, a Medusa e a Kitsune. Muito obrigada, Lucy. Não sabemos o que há pela frente ainda, mas sabemos que podemos contar com você."),
            new Line("lucy-boss3fim-01", L, "E eu sei que posso contar com vocês também. Graças às dicas de Wallace, sua guia e os reparos do Mecânico, consigo me encontrar agora, descobrir de onde vim."),
            new Line("daisy-boss3fim-02", D, "Estaremos aqui para você. Agora, com as recompensas do Mago, você pode terminar suas melhorias e continuar lutando para ficar cada vez mais forte!"));
        Dialog_SO hubFinal = Write("Daisy-hub-final",
            new Line("daisy-final-01", D, "A sua presença fez toda a diferença, Lucy. Continue lutando e fique mais forte. Siga seu caminho; estaremos aqui para você."));
        Dialog_SO vendorMage = Write("Mecanico-pos-mago",
            new Line("mecanico-mago-01", M, "Então você conseguiu a \"Esmeralda\"... Derrotar o mago não deve ter sido fácil, mas agora estamos todos livres e temos que agradecer a você por isso. Vamos fazer a melhor melhoria possível agora!"));

        AssetDatabase.SaveAssets();

        WireVendor(vendorMedusa, vendorKitsune, vendorMage);
        WireHubDaisy(hubKeepGoing, hubFinal);

        Debug.Log("[Roteiro] Falas aplicadas.");
    }

    private static Dialog_SO Write(string assetName, params Line[] lines)
    {
        string path = $"{DialogFolder}/{assetName}.asset";
        Dialog_SO dialog = AssetDatabase.LoadAssetAtPath<Dialog_SO>(path);
        if (dialog == null)
        {
            dialog = ScriptableObject.CreateInstance<Dialog_SO>();
            AssetDatabase.CreateAsset(dialog, path);
        }

        dialog.dialogs = new List<DialogItem>();
        for (int i = 0; i < lines.Length; i++)
        {
            dialog.dialogs.Add(new DialogItem
            {
                dialogId = lines[i].id,
                dialogContent = lines[i].text,
                nextDialog = i + 1 < lines.Length ? lines[i + 1].id : "",
                triggerEvent = lines[i].trigger,
                dialogDuration = 0,
                dialogOptions = new List<DialogOption>(),
                dialogEntity = Entity(lines[i].entity)
            });
        }

        EditorUtility.SetDirty(dialog);
        return dialog;
    }

    private static Entity_SO Entity(string name)
    {
        Entity_SO entity = AssetDatabase.LoadAssetAtPath<Entity_SO>($"Assets/ObjectData/Entity/{name}.asset");
        if (entity == null) Debug.LogError($"[Roteiro] Entity '{name}' nao encontrada.");
        return entity;
    }

    private static Dialog_SO Dialog(string assetName)
    {
        return AssetDatabase.LoadAssetAtPath<Dialog_SO>($"{DialogFolder}/{assetName}.asset");
    }

    /// <summary>The vendor greets with the line of the last boss beaten, then falls back to its usual line.</summary>
    private static void WireVendor(Dialog_SO medusa, Dialog_SO kitsune, Dialog_SO mage)
    {
        Dialog_SO generic = Dialog("Upgrade-Mecanico");
        GameObject root = PrefabUtility.LoadPrefabContents(VendorPrefabPath);
        try
        {
            NPC npc = root.GetComponent<NPC>();
            var so = new SerializedObject(npc);
            SerializedProperty rules = so.FindProperty("_dialogRules");
            rules.ClearArray();
            AddRule(rules, "missao-boss-3", mage, generic);
            AddRule(rules, "missao-boss-2", kitsune, generic);
            AddRule(rules, "missao-boss", medusa, generic);
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, VendorPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void AddRule(SerializedProperty rules, string taskId, Dialog_SO dialog, Dialog_SO repeat)
    {
        int index = rules.arraySize;
        rules.InsertArrayElementAtIndex(index);
        SerializedProperty rule = rules.GetArrayElementAtIndex(index);
        rule.FindPropertyRelative("taskId").stringValue = taskId;
        rule.FindPropertyRelative("status").enumValueIndex = (int)TaskManager.TaskStatus.Finished;
        rule.FindPropertyRelative("requiresItems").boolValue = false;
        rule.FindPropertyRelative("dialog").objectReferenceValue = dialog;
        rule.FindPropertyRelative("repeatDialog").objectReferenceValue = repeat;
    }

    /// <summary>Gives the Hub Daisy her short line for when the player talks to her again after a boss.</summary>
    private static void WireHubDaisy(Dialog_SO keepGoing, Dialog_SO final)
    {
        var repeats = new Dictionary<Dialog_SO, Dialog_SO>
        {
            { Dialog("Missao-boss-finalizada"), keepGoing },
            { Dialog("Missao-boss-2-finalizada"), keepGoing },
            { Dialog("Missao-boss-3-finalizada"), final }
        };

        UnityEngine.SceneManagement.Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.path != HubScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("[Roteiro] Cancelado: a cena Hub precisa estar aberta para ligar as falas da Daisy.");
                return;
            }
            scene = EditorSceneManager.OpenScene(HubScenePath, OpenSceneMode.Single);
        }

        int wired = 0;
        foreach (NPC npc in Object.FindObjectsByType<NPC>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(npc);
            SerializedProperty rules = so.FindProperty("_dialogRules");
            for (int i = 0; i < rules.arraySize; i++)
            {
                SerializedProperty rule = rules.GetArrayElementAtIndex(i);
                if (rule.FindPropertyRelative("dialog").objectReferenceValue is Dialog_SO dialog
                    && repeats.TryGetValue(dialog, out Dialog_SO repeat))
                {
                    rule.FindPropertyRelative("repeatDialog").objectReferenceValue = repeat;
                    wired++;
                }
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        if (wired == 0) Debug.LogError("[Roteiro] Nenhuma regra da Daisy encontrada na Hub.");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[Roteiro] Hub: {wired} regras da Daisy com fala de repeticao.");
    }
}
