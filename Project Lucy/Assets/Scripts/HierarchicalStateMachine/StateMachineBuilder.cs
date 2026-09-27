using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HierarchicalStateMachine
{
    public class StateMachineBuilder
    {
        private readonly State root;
        
        public StateMachineBuilder(State root)
        {
            this.root = root;
        }

        public StateMachine Build()
        {
            var m = new StateMachine(root);
            Wire(root, m, new HashSet<State>());
            return m;
        }

        private void Wire(State s, StateMachine sm, HashSet<State> visited)
        {
            if (s == null) return;
            if (!visited.Add(s)) return; // State is already wired
            
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
            var machineField = typeof(State).GetField("Machine", flags);
            if (machineField != null) machineField.SetValue(s, sm);

            foreach (var field in s.GetType().GetFields(flags))
            {
                if (field.Name == "Parent" || field.Name == "ActiveChild") continue; // Skip back-edges

                // Children built from data (e.g. one attack per asset) live in an array or list.
                if (typeof(IEnumerable<State>).IsAssignableFrom(field.FieldType))
                {
                    if (field.GetValue(s) is IEnumerable<State> children)
                    {
                        foreach (State listed in children) WireChild(s, listed, sm, visited);
                    }
                    continue;
                }

                if (!typeof(State).IsAssignableFrom(field.FieldType)) continue; // Only consider fields that are States
                WireChild(s, (State)field.GetValue(s), sm, visited);
            }
        }

        private void WireChild(State parent, State child, StateMachine sm, HashSet<State> visited)
        {
            if (child == null) return;
            if (!ReferenceEquals(child.Parent, parent)) return; // Ensure it's actually our direct child

            Wire(child, sm, visited); // Recurse into the child
        }
    }
}