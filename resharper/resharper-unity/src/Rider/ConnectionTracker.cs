﻿using System;
using JetBrains.Application.Threading;
using JetBrains.Collections.Viewable;
using JetBrains.Core;
using JetBrains.DataFlow;
using JetBrains.Lifetimes;
using JetBrains.Platform.Unity.EditorPluginModel;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Plugins.Unity.ProjectModel;
using JetBrains.Rider.Model;
using JetBrains.Util;
using ILogger = JetBrains.Util.ILogger;

namespace JetBrains.ReSharper.Plugins.Unity.Rider
{
    [SolutionComponent]
    public class ConnectionTracker
    {
        public readonly IProperty<UnityEditorState> State;

        public ConnectionTracker(Lifetime lifetime, ILogger logger, UnityHost host, UnityEditorProtocol editorProtocol,
            IThreading locks, UnitySolutionTracker unitySolutionTracker)
        {
            State = new Property<UnityEditorState>(lifetime, "UnityEditorPlugin::ConnectionState", UnityEditorState.Disconnected);
            
            if (locks.Dispatcher.IsAsyncBehaviorProhibited)
                return;

            unitySolutionTracker.IsUnityProject.AdviseOnce(lifetime, args =>
            {
                if (!args)
                    return;

                //check connection between backend and unity editor
                locks.QueueRecurring(lifetime, "PeriodicallyCheck", TimeSpan.FromSeconds(1), () =>
                {
                    var model = editorProtocol.UnityModel.Value;
                    if (model == null)
                    {
                        State.SetValue(UnityEditorState.Disconnected);
                    }
                    else
                    {
                        var rdTask = model.GetUnityEditorState.Start(Unit.Instance);
                        rdTask?.Result.Advise(lifetime, result =>
                        {
                            State.SetValue(result.Result);
                            logger.Trace($"myIsConnected = {State.Value}");
                            logger.Trace($"Inside Result. Sending connection state. State: {State.Value}");
                            host.PerformModelAction(m => m.EditorState.Value = Wrap(State.Value));
                        });
                    }

                    logger.Trace($"Sending connection state. State: {State.Value}");
                    host.PerformModelAction(m => m.EditorState.Value = Wrap(State.Value));
                });
            });
        }

        private EditorState Wrap(UnityEditorState state)
        {
            switch (state)
            {
                case UnityEditorState.Disconnected:
                    return EditorState.Disconnected;
                case UnityEditorState.Idle:
                    return EditorState.ConnectedIdle;
                case UnityEditorState.Play:
                    return EditorState.ConnectedPlay;
                case UnityEditorState.Pause:
                    return EditorState.ConnectedPause;
                case UnityEditorState.Refresh:
                    return EditorState.ConnectedRefresh;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
        }

        public bool IsConnectionEstablished()
        {
            return State.Value != UnityEditorState.Refresh && State.Value != UnityEditorState.Disconnected;
        }
    }
}