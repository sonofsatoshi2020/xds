using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NBitcoin.Rules;

namespace UnnamedCoin.Bitcoin.Consensus.PerformanceCounters.Rules
{
    /// <summary>Snapshot of rule's performance.</summary>
    public class ConsensusRulesPerformanceSnapshot
    {
        internal ConsensusRulesPerformanceSnapshot(IEnumerable<RuleItem> rulesToTrack)
        {
            this.PerformanceInfo = new Dictionary<IConsensusRuleBase, RulePerformance>();

            foreach (var rule in rulesToTrack)
            {
                var perf = new RulePerformance(rule);
                this.PerformanceInfo.Add(rule.RuleReferenceInstance, perf);
            }
        }

        internal Dictionary<IConsensusRuleBase, RulePerformance> PerformanceInfo { get; }

        public override string ToString()
        {
            var builder = new StringBuilder();

            builder.AppendLine();
            builder.AppendLine("======ConsensusRules Bench======");

            if (this.PerformanceInfo.All(x => x.Value.CalledTimes == 0))
            {
                builder.AppendLine("No samples...");
                return builder.ToString();
            }

            foreach (var rulesGroup in this.PerformanceInfo.Values.GroupBy(x => x.RuleType))
            {
                var ruleGroupTotalCalls = rulesGroup.Max(x => x.CalledTimes);

                if (ruleGroupTotalCalls == 0)
                    continue;

                var totalExecutionTimeTicks = rulesGroup.Sum(x => x.ExecutionTimesTicks);
                var avgGroupExecutionTimeMs =
                    Math.Round(TimeSpan.FromTicks(totalExecutionTimeTicks / ruleGroupTotalCalls).TotalMilliseconds, 4);

                builder.AppendLine(
                    $"{rulesGroup.Key} validation rules. Average total execution time: {avgGroupExecutionTimeMs} ms.");

                foreach (var rule in rulesGroup)
                {
                    if (rule.CalledTimes == 0)
                    {
                        builder.AppendLine($"    {rule.RuleName.PadRight(50, '-')}{"No Samples".PadRight(12, '-')}");
                        continue;
                    }

                    var avgExecutionTimeMs =
                        Math.Round(TimeSpan.FromTicks(rule.ExecutionTimesTicks / rule.CalledTimes).TotalMilliseconds,
                            4);

                    // % from average execution time for the group.
                    var percentage = Math.Round(avgExecutionTimeMs / avgGroupExecutionTimeMs * 100.0);

                    builder.AppendLine(
                        $"    {rule.RuleName.PadRight(50, '-')}{(avgExecutionTimeMs + " ms").PadRight(12, '-')}{percentage} %");
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }
    }

    /// <summary>Container of info related to a consensus rule.</summary>
    class RuleItem
    {
        public string RuleName { get; set; }

        public RuleType RuleType { get; set; }

        public IConsensusRuleBase RuleReferenceInstance { get; set; }
    }

    /// <summary>
    ///     Container of general information about the consensus rule as well as
    ///     amount of times it was executed and sum of execution times.
    /// </summary>
    /// <seealso cref="RuleItem" />
    class RulePerformance : RuleItem
    {
        public int CalledTimes;

        public long ExecutionTimesTicks;

        public RulePerformance(RuleItem rule)
        {
            this.RuleName = rule.RuleName;
            this.RuleType = rule.RuleType;
            this.RuleReferenceInstance = rule.RuleReferenceInstance;

            this.CalledTimes = 0;
            this.ExecutionTimesTicks = 0;
        }
    }

    public enum RuleType
    {
        Header,
        Integrity,
        Partial,
        Full
    }
}