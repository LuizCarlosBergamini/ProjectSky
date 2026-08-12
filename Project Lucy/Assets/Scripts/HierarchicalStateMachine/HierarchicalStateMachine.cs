using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace HierarchicalStateMachine
{
    public class State 
    {
        public readonly StateMachine Machine;
        public State Parent;
        public State ActiveChild;

        public State(StateMachine machine, State parent = null)
        {
            Machine = machine;
            Parent = parent;
        }

        protected virtual State GetInitialState() => null;
        protected virtual State GetTransition() => null;
        
        protected virtual void OnEnter() { }
        protected virtual void OnExit() { }
        protected virtual void OnUpdate(float deltaTime) { }
        protected virtual void OnFixedUpdate(float fixedDeltaTime) { }

        internal void Enter()
        {
            if (Parent != null) Parent.ActiveChild = this;
            OnEnter();
            State init = GetInitialState();
            if (init != null) init.Enter();
        }

        internal void Exit()
        {
            if (ActiveChild != null) ActiveChild.Exit();
            ActiveChild = null;
            OnExit();
        }

        internal void Update(float deltaTime)
        {
            State t = GetTransition();
            if (t != null)
            {
                Machine.Sequencer.RequestTransition(this, t);
                return;
            }
            
            if (ActiveChild != null) ActiveChild.Update(deltaTime);
            OnUpdate(deltaTime);
        }

        internal void FixedUpdate(float fixedDeltaTime)
        {
            if (ActiveChild != null) ActiveChild.FixedUpdate(fixedDeltaTime);
            OnFixedUpdate(fixedDeltaTime);
        }

        public State Leaf()
        {
            State s = this;
            while (s.ActiveChild != null) s = s.ActiveChild;
            return s;
        }

        public IEnumerable<State> PathToRoot()
        {
            for (State s = this; s != null; s = s.Parent) yield return s;
        }
    }

    public class StateMachine
    {
        public readonly State Root;
        public readonly TransitionSequencer Sequencer;
        bool _started;

        public StateMachine(State root)
        {
            Root = root;
            Sequencer = new TransitionSequencer(this);
        }

        public void Start()
        {
            if (_started) return;

            _started = true;
            Root.Enter();
        }

        public void Tick(float deltaTime)
        {
            if (!_started) Start();
            InternalTick(deltaTime);
        }
        
        public void InternalTick(float deltaTime) => Root.Update(deltaTime);

        public void ChangeState(State from, State to)
        {
            if (from == to || from == null || to == null) return;
            
            State lca = TransitionSequencer.Lca(from, to);
            
            for (State s = from; s != null && s != lca; s = s.Parent) s.Exit();
            
            var stack = new Stack<State>();
            for (State s = to; s != null; s = s.Parent) stack.Push(s);
            while (stack.Count > 0) stack.Pop().Enter();
        }
    }

    public class TransitionSequencer
    {
        public readonly StateMachine Machine;

        public TransitionSequencer(StateMachine machine)
        {
            Machine = machine;
        }

        public void RequestTransition(State from, State to)
        {
            Machine.ChangeState(from, to);
        }

        public static State Lca(State a, State b)
        {
            var ap = new HashSet<State>();
            for (var s = a; s != null; s = s.Parent) ap.Add(s);
            
            for (var s = b; s != null; s = s.Parent) 
                if (ap.Contains(s)) return s;
            
            return null;
        }
    }
}
    
