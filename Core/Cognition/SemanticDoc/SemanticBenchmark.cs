// =============================================================================
// SemanticDoc/SemanticBenchmark.cs — benchmark types + 40 predefined queries
// =============================================================================
// Purpose: Quantitative evaluation of semantic retrieval quality.
// =============================================================================

using System.Text.Json;

namespace Core.Cognition.SemanticDoc;

public sealed class SemanticBenchmarkCase
{
    public required string CaseId { get; init; }
    public required string Query { get; init; }
    public required IReadOnlyList<ExpectedResult> Expected { get; init; }
    public QueryType QueryType { get; init; }
    public BenchmarkDifficulty Difficulty { get; init; }

    public double MinRecallAt5 { get; init; } = 0.80;
    public double MinRecallAt10 { get; init; } = 0.90;
    public double MaxNoiseRatio { get; init; } = 0.40;
}

public sealed class ExpectedResult
{
    public required string MethodName { get; init; }
    public int Priority { get; init; } = 1;
}

public enum BenchmarkDifficulty { Easy = 0, Medium = 1, Hard = 2 }

public enum QueryType
{
    Database = 0, DTO = 1, Exception = 2, HTTP = 3,
    Architecture = 4, BugAnalysis = 5, CodeModification = 6, BusinessWorkflow = 7,
}

// ═══════════════════════════════════════════════════════════════
// Predefined ZhiFang Benchmark Queries (40 cases)
// ═══════════════════════════════════════════════════════════════

public static class ZhiFangBenchmarkQueries
{
    public static IReadOnlyList<SemanticBenchmarkCase> Build()
    {
        var cases = new List<SemanticBenchmarkCase>();

        // ── Easy: Database / Table queries (15 cases) ──
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-001", Query = "谁写入 LB_MICROCULTURE", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "SaveCultureResult", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-002", Query = "哪些地方查询 LBQCItem", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] {
                new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 1 },
                new ExpectedResult { MethodName = "SearchListByHQL", Priority = 2 },
            },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-003", Query = "谁访问 LBEquip", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "GetListByEQALinkLab", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-004", Query = "哪些方法操作 LBQCMaterial", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-005", Query = "谁查询 LBQCItemTime", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-006", Query = "INSERT INTO LB_MICROCULTURE", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "SaveCultureResult", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-007", Query = "哪些方法访问 EquipID 字段", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 2 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-008", Query = "谁写入审核日志", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "AddLisOperate", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-009", Query = "SearchListByHQL 查询哪些表", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "SearchListByHQL", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-010", Query = "哪些地方执行 HQL 查询", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "SearchListByHQL", Priority = 1 }, new ExpectedResult { MethodName = "GetListByHQL", Priority = 2 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-011", Query = "谁访问 LBQCItem 表", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-012", Query = "哪些方法涉及 DataTimeStamp", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 2 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-013", Query = "谁写入质检数据", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "Save", Priority = 1 }, new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 2 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-014", Query = "哪些方法调用 DBDao.Save", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "Save", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zdb-015", Query = "GetListByHQL 方法", Difficulty = BenchmarkDifficulty.Easy, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "GetListByHQL", Priority = 1 }, new ExpectedResult { MethodName = "SearchListByHQL", Priority = 2 } },
        });

        // ── Medium: DTO / Exception / HTTP / Architecture (20 cases) ──
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-001", Query = "哪些方法使用 BaseResultDataValue", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.DTO,
            Expected = new[] { new ExpectedResult { MethodName = "QC_UDTO_CopyLBQCItemByMatID", Priority = 1 }, new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-002", Query = "哪些接口处理质控物复制", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.HTTP,
            Expected = new[] { new ExpectedResult { MethodName = "QC_UDTO_CopyLBQCItemByMatID", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-003", Query = "哪里捕获异常", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.Exception,
            Expected = new[] { new ExpectedResult { MethodName = "QC_UDTO_CopyLBQCItemByMatID", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-004", Query = "哪里记录操作日志", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.BusinessWorkflow,
            Expected = new[] { new ExpectedResult { MethodName = "AddLisOperate", Priority = 1 }, new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 2 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-005", Query = "培养结果保存链路", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.Architecture,
            Expected = new[] { new ExpectedResult { MethodName = "QC_UDTO_CopyLBQCItemByMatID", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-006", Query = "权限验证在哪里执行", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.BusinessWorkflow,
            Expected = new[] { new ExpectedResult { MethodName = "QCWriteOperation", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-007", Query = "哪里使用 ZhiFang.Common.Log.Log", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.BusinessWorkflow,
            Expected = new[] { new ExpectedResult { MethodName = "QC_UDTO_CopyLBQCItemByMatID", Priority = 1 }, new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-008", Query = "质控项目编号怎么计算的", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.BusinessWorkflow,
            Expected = new[] { new ExpectedResult { MethodName = "GetQCItemNo", Priority = 1 }, new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 2 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-009", Query = "EntityList 用在哪些方法", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.DTO,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 2 }, new ExpectedResult { MethodName = "SearchListByHQL", Priority = 2 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-010", Query = "哪些方法处理仪器信息", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.BusinessWorkflow,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 2 }, new ExpectedResult { MethodName = "GetListByEQALinkLab", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-011", Query = "ClassMapperHelp 在哪里使用", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.CodeModification,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-012", Query = "JsonDotNetSerializer 序列化在哪里", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.CodeModification,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-013", Query = "哪些 Controller 方法调用 BLL", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.Architecture,
            Expected = new[] { new ExpectedResult { MethodName = "QC_UDTO_CopyLBQCItemByMatID", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-014", Query = "审核流程涉及哪些方法", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.BusinessWorkflow,
            Expected = new[] { new ExpectedResult { MethodName = "AddLisOperate", Priority = 1 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-015", Query = "哪些方法使用了 CookieHelper", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.CodeModification,
            Expected = new[] { new ExpectedResult { MethodName = "GetDataRowRoleHQLString", Priority = 2 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-016", Query = "FilterMacroCommand 在哪里调用", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.CodeModification,
            Expected = new[] { new ExpectedResult { MethodName = "GetListByHQL", Priority = 2 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-017", Query = "HttpContextHelper 使用位置", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.CodeModification,
            Expected = new[] { new ExpectedResult { MethodName = "GetDataRowRoleHQLString", Priority = 2 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-018", Query = "IApplicationContext 依赖注入", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.Architecture,
            Expected = new[] { new ExpectedResult { MethodName = "GetDataRowRoleHQLString", Priority = 2 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-019", Query = "HibernateTemplate.Execute 调用", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.Database,
            Expected = new[] { new ExpectedResult { MethodName = "GetListByHQL", Priority = 1 }, new ExpectedResult { MethodName = "SearchListByHQL", Priority = 2 } },
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zmd-020", Query = "ConfigHelper.GetConfigString 在哪里", Difficulty = BenchmarkDifficulty.Medium, QueryType = QueryType.CodeModification,
            Expected = new[] { new ExpectedResult { MethodName = "GetDataRowRoleHQLString", Priority = 2 } },
        });

        // ── Hard: Vague business semantics / Root cause (10+5 cases) ──
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-001", Query = "培养结果保存后为什么没有通知审核", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.BugAnalysis,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 1 }, new ExpectedResult { MethodName = "AddLisOperate", Priority = 1 } },
            MaxNoiseRatio = 0.50,
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-002", Query = "复制质控物时为什么仪器项目丢失", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.BugAnalysis,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 1 } },
            MaxNoiseRatio = 0.50,
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-003", Query = "审核通过后哪些地方会更新状态", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.BusinessWorkflow,
            Expected = new[] { new ExpectedResult { MethodName = "AddLisOperate", Priority = 1 } },
            MaxNoiseRatio = 0.50,
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-004", Query = "哪里处理培养基相关逻辑", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.BusinessWorkflow,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 2 } },
            MaxNoiseRatio = 0.60,
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-005", Query = "谁负责质控同步", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.BusinessWorkflow,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 2 } },
            MaxNoiseRatio = 0.60,
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-006", Query = "为什么报告没有生成", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.BugAnalysis,
            Expected = new[] { new ExpectedResult { MethodName = "QC_UDTO_CopyLBQCItemByMatID", Priority = 2 } },
            MaxNoiseRatio = 0.60,
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-007", Query = "修改培养结果时需要改哪些地方", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.CodeModification,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 1 } },
            MaxNoiseRatio = 0.50,
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-008", Query = "仪器信息怎么同步到质控物", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.BusinessWorkflow,
            Expected = new[] { new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 2 } },
            MaxNoiseRatio = 0.60,
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-009", Query = "数据权限过滤逻辑在哪里", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.Architecture,
            Expected = new[] { new ExpectedResult { MethodName = "GetDataRowRoleHQLString", Priority = 1 } },
            MaxNoiseRatio = 0.50,
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-010", Query = "LabID 多实验室隔离怎么实现的", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.Architecture,
            Expected = new[] { new ExpectedResult { MethodName = "GetDataRowRoleHQLString", Priority = 1 } },
            MaxNoiseRatio = 0.60,
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-011", Query = "质控项目复制完整流程", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.BusinessWorkflow,
            Expected = new[] {
                new ExpectedResult { MethodName = "QC_UDTO_CopyLBQCItemByMatID", Priority = 1 },
                new ExpectedResult { MethodName = "AddCopyLBQCItemByMatID", Priority = 1 },
                new ExpectedResult { MethodName = "AddLisOperate", Priority = 2 },
            },
            MaxNoiseRatio = 0.50,
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-012", Query = "哪些地方依赖 Spring ContextRegistry", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.Architecture,
            Expected = new[] { new ExpectedResult { MethodName = "GetDataRowRoleHQLString", Priority = 1 } },
            MaxNoiseRatio = 0.50,
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-013", Query = "多线程异步 ThreadLocal 数据隔离", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.Architecture,
            Expected = new[] { new ExpectedResult { MethodName = "GetDataRowRoleHQLString", Priority = 2 } },
            MaxNoiseRatio = 0.60,
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-014", Query = "NHibernate Session 工厂在哪里创建", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.Architecture,
            Expected = new[] { new ExpectedResult { MethodName = "GetListByEQALinkLab", Priority = 2 } },
            MaxNoiseRatio = 0.60,
        });
        cases.Add(new SemanticBenchmarkCase
        {
            CaseId = "zhd-015", Query = "BaseBLL Save 方法调用链", Difficulty = BenchmarkDifficulty.Hard, QueryType = QueryType.Architecture,
            Expected = new[] { new ExpectedResult { MethodName = "Save", Priority = 1 } },
            MaxNoiseRatio = 0.50,
        });

        return cases;
    }
}
