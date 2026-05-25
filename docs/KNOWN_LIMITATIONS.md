# Known Limitations

## Static Analysis

- 反射调用不可见（`MethodInfo.Invoke`, `Activator.CreateInstance`）
- 运行时 DI 绑定不可见（`services.AddScoped<IFoo, Foo>()` 未解析 — Phase 7C 计划）
- AOP/动态代理不可见
- 异步/多线程调度关系不可见
- 事件/委托绑定不可见

## Method Resolution

- 同文件 private 方法调用已通过字符串回退连接，置信度 medium
- 跨项目引用依赖 Roslyn 元数据解析，高泛型场景可能漏连
- 虚方法/接口方法的运行时实现选择不可见

## ORM Coverage

- NHibernate: 已支持 `session.Query<T>()`、HQL、HBM XML
- NHibernate: FluentNHibernate 代码映射未解析
- Entity Framework Core: 未解析 DbContext
- Dapper: 未解析原始 SQL

## DI Framework

- Spring.NET XML: ✅ 已支持
- Spring.NET context.GetObject: ✅ 已支持（SpringContextObjectAnalyzer）
- Microsoft.Extensions.DI: ❌ 未解析
- Autofac: ❌ 未解析

## Token Usage

- 代码图构建会追加语法回退边（max 5000）和同 class 连接边（max 3000）
- 大项目（2500+ 方法）扫描时间 30-60 秒
- 缓存恢复 < 2 秒

## Performance

- 首次扫描 25 项目 2500 方法约 30-60 秒
- SemanticModelProvider 现在使用延迟编译，扫描时每文件单文件编译（快）
- 语法回退边用纯字符串匹配，不用 Roslyn AST 解析
- 缓存恢复 < 2 秒

## Web UI

- 代码修复功能需要配置有效 LLM API Key
- Ollama 本地模型需要预先安装并运行
- 前端仅开发模式（Vite dev server），生产构建未配置
