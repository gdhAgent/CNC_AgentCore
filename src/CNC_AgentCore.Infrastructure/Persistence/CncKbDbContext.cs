// Infrastructure/Persistence/CncKbDbContext.cs —— 三 schema（kb/ops/log）
using CNC_AgentCore.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CNC_AgentCore.Infrastructure.Persistence;

public sealed class CncKbDbContext : DbContext
{
    public CncKbDbContext(DbContextOptions<CncKbDbContext> opts) : base(opts) { }

    // kb schema
    public DbSet<Alarm> Alarms => Set<Alarm>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<Chunk> Chunks => Set<Chunk>();
    public DbSet<TermDictEntry> TermDict => Set<TermDictEntry>();
    public DbSet<ImportJob> ImportJobs => Set<ImportJob>();

    // ops schema
    public DbSet<User> Users => Set<User>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<MaintenanceLog> MaintenanceLogs => Set<MaintenanceLog>();
    public DbSet<BaseItem> BaseItems => Set<BaseItem>();

    // log schema
    public DbSet<QueryLog> QueryLogs => Set<QueryLog>();
    public DbSet<QueryTraceStep> QueryTraceSteps => Set<QueryTraceStep>();
    public DbSet<Suggestion> Suggestions => Set<Suggestion>();
    public DbSet<Feedback> Feedbacks => Set<Feedback>();
    public DbSet<EvalItem> EvalItems => Set<EvalItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        // ----- kb schema -----
        b.Entity<Alarm>(e =>
        {
            e.ToTable("alarms", "kb");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.Brand).HasColumnName("brand").HasMaxLength(64);
            e.Property(x => x.Controller).HasColumnName("controller").HasMaxLength(64);
            e.Property(x => x.CodeNorm).HasColumnName("code_norm").HasMaxLength(64).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(64);
            e.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(32);
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.Cause).HasColumnName("cause");
            e.Property(x => x.Action).HasColumnName("action");
            e.Property(x => x.SafetyNote).HasColumnName("safety_note");
            e.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector(1024)");
            e.Property(x => x.Tsv).HasColumnName("tsv").HasColumnType("tsvector");
            e.Property(x => x.IsDemo).HasColumnName("is_demo");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.CodeNorm).IsUnique();
        });

        b.Entity<Document>(e =>
        {
            e.ToTable("documents", "kb");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.Title).HasColumnName("title").IsRequired();
            e.Property(x => x.Brand).HasColumnName("brand").HasMaxLength(64);
            e.Property(x => x.MachineModel).HasColumnName("machine_model").HasMaxLength(64);
            e.Property(x => x.DocType).HasColumnName("doc_type").HasMaxLength(32);
            e.Property(x => x.SourcePath).HasColumnName("source_path");
            e.Property(x => x.Hash).HasColumnName("hash").HasMaxLength(128);
            e.Property(x => x.IsDemo).HasColumnName("is_demo");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        b.Entity<Chunk>(e =>
        {
            e.ToTable("chunks", "kb");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.DocId).HasColumnName("doc_id");
            e.Property(x => x.Level).HasColumnName("level");
            e.Property(x => x.ParentId).HasColumnName("parent_id");
            e.Property(x => x.HeadingPath).HasColumnName("heading_path");
            e.Property(x => x.Content).HasColumnName("content").IsRequired();
            e.Property(x => x.PageFrom).HasColumnName("page_from");
            e.Property(x => x.PageTo).HasColumnName("page_to");
            e.Property(x => x.Embedding).HasColumnName("embedding").HasColumnType("vector(1024)");
            e.Property(x => x.Tsv).HasColumnName("tsv").HasColumnType("tsvector");
        });

        b.Entity<TermDictEntry>(e =>
        {
            e.ToTable("term_dict", "kb");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.Canonical).HasColumnName("canonical").IsRequired();
            e.Property(x => x.Synonyms).HasColumnName("synonyms").HasColumnType("text[]");
            e.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(32);
            e.Property(x => x.Lang).HasColumnName("lang").HasMaxLength(8);
        });

        b.Entity<ImportJob>(e =>
        {
            e.ToTable("import_jobs", "kb");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.JobType).HasColumnName("job_type").IsRequired();
            e.Property(x => x.SourcePath).HasColumnName("source_path");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(16);
            e.Property(x => x.TotalRows).HasColumnName("total_rows");
            e.Property(x => x.ProcessedRows).HasColumnName("processed_rows");
            e.Property(x => x.FailedRows).HasColumnName("failed_rows");
            e.Property(x => x.ErrorMessage).HasColumnName("error_message");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.FinishedAt).HasColumnName("finished_at");
        });

        // ----- ops schema -----
        b.Entity<User>(e =>
        {
            e.ToTable("users", "ops");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(64).IsRequired();
            e.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(128).IsRequired();
            e.Property(x => x.PasswordHash).HasColumnName("password_hash").HasMaxLength(256).IsRequired();
            e.Property(x => x.Role).HasColumnName("role").HasMaxLength(32).IsRequired();
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.LastLoginAt).HasColumnName("last_login_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.CreatedBy).HasColumnName("created_by");
            e.HasIndex(x => x.Username).IsUnique();
        });

        b.Entity<RolePermission>(e =>
        {
            e.ToTable("role_permissions", "ops");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.Role).HasColumnName("role").HasMaxLength(32).IsRequired();
            e.Property(x => x.PageCode).HasColumnName("page_code").HasMaxLength(64).IsRequired();
            e.Property(x => x.CanAccess).HasColumnName("can_access");
            e.Property(x => x.Actions).HasColumnName("actions").HasColumnType("text[]");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.Property(x => x.UpdatedBy).HasColumnName("updated_by");
            e.HasIndex(x => new { x.Role, x.PageCode }).IsUnique();
        });

        b.Entity<Machine>(e =>
        {
            e.ToTable("machines", "ops");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.AssetNo).HasColumnName("asset_no").HasMaxLength(32).IsRequired();
            e.Property(x => x.Name).HasColumnName("name").IsRequired();
            e.Property(x => x.Brand).HasColumnName("brand").HasMaxLength(64);
            e.Property(x => x.Model).HasColumnName("model").HasMaxLength(64);
            e.Property(x => x.Controller).HasColumnName("controller").HasMaxLength(64);
            e.Property(x => x.Location).HasColumnName("location").HasMaxLength(64);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(32);
            e.Property(x => x.IsDemo).HasColumnName("is_demo");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        b.Entity<MaintenanceLog>(e =>
        {
            e.ToTable("maintenance_logs", "ops");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.MachineId).HasColumnName("machine_id");
            e.Property(x => x.OrderNo).HasColumnName("order_no").HasMaxLength(32).IsRequired();
            e.Property(x => x.AlarmCode).HasColumnName("alarm_code").HasMaxLength(32);
            e.Property(x => x.FaultType).HasColumnName("fault_type").HasMaxLength(64);
            e.Property(x => x.Symptom).HasColumnName("symptom");
            e.Property(x => x.RootCause).HasColumnName("root_cause");
            e.Property(x => x.ActionTaken).HasColumnName("action_taken");
            e.Property(x => x.Engineer).HasColumnName("engineer").HasMaxLength(64);
            e.Property(x => x.DowntimeMin).HasColumnName("downtime_min");
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.IsDemo).HasColumnName("is_demo");
        });

        b.Entity<BaseItem>(e =>
        {
            e.ToTable("base_items", "ops");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(64).IsRequired();
            e.Property(x => x.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
            e.Property(x => x.Label).HasColumnName("label").HasMaxLength(128).IsRequired();
            e.Property(x => x.Description).HasColumnName("description");
            e.Property(x => x.SortOrder).HasColumnName("sort_order");
            e.Property(x => x.IsActive).HasColumnName("is_active");
        });

        // ----- log schema -----
        b.Entity<QueryLog>(e =>
        {
            e.ToTable("query_logs", "log");
            e.HasKey(x => x.TraceId);
            e.Property(x => x.TraceId).HasColumnName("trace_id");
            e.Property(x => x.Query).HasColumnName("query");
            e.Property(x => x.Route).HasColumnName("route").HasMaxLength(32);
            e.Property(x => x.Refused).HasColumnName("refused");
            e.Property(x => x.RefusedReason).HasColumnName("refused_reason").HasMaxLength(64);
            e.Property(x => x.Answer).HasColumnName("answer");
            e.Property(x => x.Rounds).HasColumnName("rounds");
            e.Property(x => x.Degraded).HasColumnName("degraded");
            e.Property(x => x.TotalMs).HasColumnName("total_ms");
            e.Property(x => x.InputTokens).HasColumnName("input_tokens");
            e.Property(x => x.OutputTokens).HasColumnName("output_tokens");
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(64);
            e.Property(x => x.Ip).HasColumnName("ip").HasMaxLength(64);
            e.Property(x => x.Retrieved).HasColumnName("retrieved").HasColumnType("jsonb");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(16);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        b.Entity<QueryTraceStep>(e =>
        {
            e.ToTable("query_trace_steps", "log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.TraceId).HasColumnName("trace_id");
            e.Property(x => x.Seq).HasColumnName("seq");
            e.Property(x => x.Step).HasColumnName("step").HasMaxLength(32);
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(16);
            e.Property(x => x.Ms).HasColumnName("ms");
            e.Property(x => x.Input).HasColumnName("input").HasColumnType("jsonb");
            e.Property(x => x.Output).HasColumnName("output").HasColumnType("jsonb");
            e.Property(x => x.Note).HasColumnName("note");
            e.Property(x => x.StartedAt).HasColumnName("started_at");
        });

        b.Entity<Suggestion>(e =>
        {
            e.ToTable("kb_suggestions", "log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.Source).HasColumnName("source").HasMaxLength(32);
            e.Property(x => x.SourceTraceId).HasColumnName("source_trace_id");
            e.Property(x => x.OriginalQuery).HasColumnName("original_query");
            e.Property(x => x.OriginalAnswer).HasColumnName("original_answer");
            e.Property(x => x.SuggestedTitle).HasColumnName("suggested_title");
            e.Property(x => x.SuggestedContent).HasColumnName("suggested_content");
            e.Property(x => x.Status).HasColumnName("status").HasMaxLength(16);
            e.Property(x => x.ReviewedBy).HasColumnName("reviewed_by").HasMaxLength(64);
            e.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
            e.Property(x => x.ResolvedRef).HasColumnName("resolved_ref");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        b.Entity<Feedback>(e =>
        {
            e.ToTable("feedbacks", "log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.TraceId).HasColumnName("trace_id");
            e.Property(x => x.Rating).HasColumnName("rating");
            e.Property(x => x.Comment).HasColumnName("comment");
            e.Property(x => x.Username).HasColumnName("username").HasMaxLength(64);
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });

        b.Entity<EvalItem>(e =>
        {
            e.ToTable("eval_items", "log");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id").ValueGeneratedOnAdd();
            e.Property(x => x.Query).HasColumnName("query").IsRequired();
            e.Property(x => x.ExpectedDocIds).HasColumnName("expected_doc_ids").HasColumnType("jsonb");
            e.Property(x => x.ExpectedAlarmCodes).HasColumnName("expected_alarm_codes").HasColumnType("jsonb");
            e.Property(x => x.Tags).HasColumnName("tags").HasColumnType("jsonb");
            e.Property(x => x.Category).HasColumnName("category").HasMaxLength(64);
            e.Property(x => x.IsDemo).HasColumnName("is_demo");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
        });
    }
}
