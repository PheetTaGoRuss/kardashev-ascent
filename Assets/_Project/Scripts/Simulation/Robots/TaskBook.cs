using System;
using System.Collections.Generic;

namespace KardashevAscent.Simulation.Robots
{
    /// <summary>Where a task stands (spec v2 §4).</summary>
    public enum TaskState { Available, Reserved, Travelling, Working, Paused, Blocked, AssistanceNeeded, Completed, Cancelled }

    /// <summary>
    /// How a kind of task behaves when its robot is taken off it (spec v2 §3, design D15). A task that would lose
    /// its progress is not interrupted automatically, even by a Critical task. Values are design choices.
    /// </summary>
    public readonly struct TaskPolicy
    {
        public readonly bool MayBeInterrupted;
        public readonly bool KeepsProgress;

        public TaskPolicy(bool mayBeInterrupted, bool keepsProgress)
        {
            MayBeInterrupted = mayBeInterrupted;
            KeepsProgress = keepsProgress;
        }

        /// <summary>
        /// Build, salvage, mining and Workshop work keep their progress (it lives on the site, batch or job); a survey
        /// is a measurement and starts again, so Critical tasks don't pull a robot off one.
        /// </summary>
        public static TaskPolicy For(TaskKind kind) =>
            kind == TaskKind.Survey ? new TaskPolicy(false, false) : new TaskPolicy(true, true);
    }

    /// <summary>One task as the monitor shows it.</summary>
    public sealed class TaskRecord
    {
        public WorkTask Task { get; internal set; }
        public TaskState State { get; internal set; }
        /// <summary>Why it is waiting, blocked or paused (empty when it is going fine).</summary>
        public string Reason { get; internal set; } = "";
        public int? RobotId { get; internal set; }
        public int? ReservedFor { get; internal set; }
        /// <summary>The most urgent priority any robot that could take it gives it.</summary>
        public TaskPriority Priority { get; internal set; } = TaskPriority.Normal;
        /// <summary>Game hours when it became blocked (−1: not blocked).</summary>
        public double BlockedSinceHours { get; internal set; } = -1.0;
        /// <summary>Game hours when it finished or was cancelled.</summary>
        public double EndedHours { get; internal set; }
    }

    /// <summary>A robot's assignment changed, and why (spec v2 §4 "no silent reassignment").</summary>
    public readonly struct AssignmentChange
    {
        public readonly double Hours;
        public readonly int RobotId;
        public readonly WorkTask? From;
        public readonly WorkTask? To;
        public readonly string Reason;

        public AssignmentChange(double hours, int robotId, WorkTask? from, WorkTask? to, string reason)
        {
            Hours = hours;
            RobotId = robotId;
            From = from;
            To = to;
            Reason = reason;
        }
    }

    /// <summary>
    /// The task monitor's records (spec v2 §4, §14): every open task's state and reason, recently finished ones,
    /// and when a task needs assistance (blocked for longer than <see cref="AssistanceAfterHours"/>).
    /// </summary>
    public sealed class TaskBook
    {
        /// <summary>Blocked this long, a task asks for assistance (and the player is told). TBD.</summary>
        public const double AssistanceAfterHours = 12.0;
        public const int FinishedKept = 20;

        readonly Dictionary<WorkTask, TaskRecord> open = new Dictionary<WorkTask, TaskRecord>();
        readonly List<TaskRecord> finished = new List<TaskRecord>();
        readonly HashSet<WorkTask> cancelled = new HashSet<WorkTask>();

        public IReadOnlyCollection<TaskRecord> Open => open.Values;
        /// <summary>Recently completed or cancelled tasks, newest last.</summary>
        public IReadOnlyList<TaskRecord> Finished => finished;

        public TaskRecord Get(WorkTask task) => open.TryGetValue(task, out TaskRecord r) ? r : null;

        public int Count(TaskState state)
        {
            int n = 0;
            foreach (TaskRecord r in open.Values)
                if (r.State == state) n++;
            return n;
        }

        /// <summary>The player (or the game) cancelled a task: it will be recorded as Cancelled, not Completed.</summary>
        public void MarkCancelled(WorkTask task) => cancelled.Add(task);

        internal TaskRecord Touch(WorkTask task)
        {
            if (!open.TryGetValue(task, out TaskRecord r))
                open[task] = r = new TaskRecord { Task = task };
            r.Task = task;
            return r;
        }

        /// <summary>Tasks no longer open have ended: completed, or cancelled if <see cref="MarkCancelled"/> said so.</summary>
        internal void Close(HashSet<WorkTask> stillOpen, double hours)
        {
            var gone = new List<WorkTask>();
            foreach (WorkTask t in open.Keys)
                if (!stillOpen.Contains(t)) gone.Add(t);
            foreach (WorkTask t in gone)
            {
                TaskRecord r = open[t];
                open.Remove(t);
                r.State = cancelled.Contains(t) ? TaskState.Cancelled : TaskState.Completed;
                r.Reason = "";
                r.RobotId = null;
                r.EndedHours = hours;
                finished.Add(r);
                if (finished.Count > FinishedKept) finished.RemoveAt(0);
            }
            cancelled.RemoveWhere(t => !stillOpen.Contains(t) && !open.ContainsKey(t));
        }

        internal void SetBlockedSince(WorkTask task, double hours) => Touch(task).BlockedSinceHours = hours;

        internal void Clear()
        {
            open.Clear();
            finished.Clear();
            cancelled.Clear();
        }
    }
}
