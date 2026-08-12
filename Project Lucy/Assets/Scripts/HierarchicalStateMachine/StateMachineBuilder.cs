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
                if (!typeof(State).IsAssignableFrom(field.FieldType)) continue; // Only consider fields that are States
                if (field.Name == "Parent") continue; // Skip back-edge to parent
                
                var child = (State)field.GetValue(s);
                if (child == null) continue;
                if (!ReferenceEquals(child.Parent, s)) continue; // Ensure it's actually our direct child
                
                Wire(child, sm, visited); // Recurse into the child
            }
        }
    }
}