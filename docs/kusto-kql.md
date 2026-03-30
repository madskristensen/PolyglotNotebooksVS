# Querying Kusto (KQL)

Use KQL (Kusto Query Language) cells to query Azure Data Explorer (Kusto) clusters directly from your notebook. Connect to a cluster, write KQL queries, see results as rich tables, and share query data into C#, JavaScript, or other kernels for further analysis.

---

## Prerequisites

- **An Azure Data Explorer cluster** you have access to (or a free [Azure Data Explorer sample cluster](https://dataexplorer.azure.com/clusters/help))
- **The `Microsoft.DotNet.Interactive.Kql` NuGet extension** — loaded via a `#r` directive (shown below)
- **Azure identity** — you'll authenticate via your Azure AD account

## Step 1: Load the KQL Extension

Before you can connect to a Kusto cluster, load the KQL extension into the notebook kernel. Create a **C# cell** and run:

```csharp
#r "nuget: Microsoft.DotNet.Interactive.Kql, *-*"
```

> **Tip:** The `*-*` version specifier pulls the latest preview. You can pin to a specific version (e.g., `1.0.0-beta.24101.1`) for reproducible notebooks.

This only needs to run once per kernel session. The extension registers the `#!kql` kernel and the `#!connect` magic command for Kusto.

## Step 2: Connect to a Kusto Cluster

In a new **C# cell**, use the `#!connect` magic command to establish a connection:

```csharp
#!connect kql --kernel-name myCluster --cluster "https://help.kusto.windows.net" --database "Samples"
```

| Parameter | Description |
|-----------|-------------|
| `--kernel-name` | A friendly name you choose for this connection (used in the cell kernel dropdown and `#!share`) |
| `--cluster` | The full URL of your Azure Data Explorer cluster |
| `--database` | The default database to query |

After running this cell, a browser window may open for Azure AD authentication. Once authenticated, the connection is stored in the kernel session.

> **Note:** The kernel name you choose (e.g., `myCluster`) becomes available in the cell language dropdown. Select it to write KQL in subsequent cells.

### Multiple Connections

You can connect to multiple clusters or databases in the same notebook:

```csharp
#!connect kql --kernel-name production --cluster "https://mycluster.eastus.kusto.windows.net" --database "Telemetry"
#!connect kql --kernel-name staging --cluster "https://mycluster-staging.eastus.kusto.windows.net" --database "Telemetry"
```

Then select `production` or `staging` from the cell's language dropdown to target each connection independently.

## Step 3: Run a KQL Query

Change the cell's language to your Kusto connection name (e.g., **myCluster**) using the dropdown in the cell toolbar, then write your KQL query:

```kql
StormEvents
| where State == "TEXAS"
| summarize EventCount = count() by EventType
| top 10 by EventCount desc
```

Press **Shift+Enter** to run the query. Results appear as a **formatted HTML table** directly below the cell.

### More Query Examples

**Time-series aggregation:**

```kql
StormEvents
| where StartTime between (datetime(2007-01-01) .. datetime(2007-12-31))
| summarize Events = count() by bin(StartTime, 7d)
| render timechart
```

**Joining tables:**

```kql
StormEvents
| where DamageProperty > 0
| join kind=inner (
	StormEvents | distinct State, EventType
) on State
| project State, EventType, DamageProperty
| top 20 by DamageProperty desc
```

**Using `let` statements:**

```kql
let threshold = 100;
StormEvents
| summarize EventCount = count() by State
| where EventCount > threshold
| order by EventCount desc
```

## Viewing Results

KQL query results are rendered as interactive HTML tables, just like other rich output in the notebook. For large result sets, the output is paginated and scrollable.

> **Tip:** If results are truncated, increase the **Maximum output length** in **Tools → Options → Polyglot Notebooks** (see [Settings](settings.md)).

## Sharing KQL Results with Other Kernels

One of the most powerful patterns is querying data in KQL and then processing it in C# or JavaScript. Use `#!share` to pass query results between kernels.

### Example: KQL → C#

**Cell 1 (myCluster / KQL):**

```kql
let stormData = StormEvents
| where State == "TEXAS"
| project EventType, DamageProperty, StartTime
| top 50 by DamageProperty desc;
stormData
```

**Cell 2 (C#):**

```csharp
#!share --from myCluster stormData

// stormData is now available as a collection
display(stormData);

// Process with LINQ
var totalDamage = stormData.Sum(r => r.DamageProperty);
Console.WriteLine($"Total property damage: {totalDamage:C}");
```

### Example: KQL → JavaScript for Visualization

**Cell 3 (JavaScript):**

```javascript
#!share --from myCluster stormData

// Use the data for custom rendering
console.log(`Received ${stormData.length} records from Kusto`);
console.log(JSON.stringify(stormData.slice(0, 5), null, 2));
```

See [Variable Sharing](variable-sharing.md) for more details on `#!share` syntax and limitations.

## Handling Long-Running Queries

KQL queries against large datasets or complex aggregations can take significant time — sometimes minutes. Polyglot Notebooks provides several ways to manage this.

### Stopping a Running Query

If a query is taking too long or you realize you need to modify it:

- Click the **■ Stop** button that appears in the cell toolbar while the query is running, or
- Press **Ctrl+.** (Ctrl+Period), or
- Click **Interrupt** (⏹) in the notebook toolbar

The query is cancelled, the timer stops, and the cell returns to idle. You can edit the query and run it again.

### Setting an Execution Timeout

You can configure an automatic timeout so cells are cancelled after a set duration:

1. Go to **Tools → Options → Polyglot Notebooks**
2. Set **Cell execution timeout (seconds)** to your preferred limit (e.g., `300` for 5 minutes)
3. Set to `0` (the default) to disable automatic timeout

> **Tip for KQL users:** Leave the timeout at `0` (disabled) unless you want a hard safety net. KQL queries to large clusters can legitimately take several minutes for complex aggregations or joins. If you do set a timeout, choose a generous value like 300–600 seconds.

When a cell times out, it shows an error message with the elapsed time, and the cell status changes to **Failed**.

## Tips

- **Start with the sample cluster.** Use `https://help.kusto.windows.net` with the `Samples` database to explore KQL without needing your own cluster.
- **One connection cell, many query cells.** The `#!connect` cell only needs to run once per session. After that, create as many KQL cells as you need.
- **Use `| take 10` while developing.** Limit results during query development to keep feedback fast, then remove the limit for the final run.
- **Combine KQL with Mermaid.** Query data in KQL, share to C#, transform into a Mermaid diagram definition, and render it — all in the same notebook.
- **Check the Variable Explorer.** After running a KQL query, open **View → Polyglot Variables** to see the result set and its shape.
- **Restart the kernel to reconnect.** If your Azure AD token expires during a long session, restart the kernel and re-run the connection cell.

## Troubleshooting

### "The Kusto extension is not loaded"

**Solution:** Make sure you ran the `#r "nuget: Microsoft.DotNet.Interactive.Kql, *-*"` cell before the `#!connect` cell. The extension must be loaded first.

### Authentication fails or times out

**Solutions:**

1. **Check your Azure AD credentials.** The browser authentication window must complete successfully.
2. **Verify cluster access.** Ensure your account has at least Viewer permissions on the target database.
3. **Try a different cluster URL.** Make sure the URL is correct and includes `https://`.
4. **Check your network.** Corporate firewalls may block connections to `*.kusto.windows.net`.

### Query returns no results

**Solutions:**

1. **Check the database name** in your `#!connect` command. Kusto clusters often have multiple databases.
2. **Verify the table name.** Run `.show tables` to list available tables in the connected database.
3. **Check time filters.** KQL datetime filters are case-sensitive and format-specific.

### Query is slow

**Solutions:**

1. **Add filters early.** Use `| where` clauses before aggregations to reduce the data scanned.
2. **Limit results during development.** Add `| take 100` while iterating on your query.
3. **Check the cluster health.** Cluster throttling or high load can slow queries.
4. **Use the Stop button.** If a query is taking too long, click **■ Stop** in the cell toolbar and refine your query.

## Next Steps

- [Running Code](running-code.md) — execution order, Stop button, and kernel lifecycle
- [Variable Sharing](variable-sharing.md) — pass KQL results into C#, JavaScript, and other kernels
- [Rich Output](rich-output.md) — display tables, charts, and diagrams from your data
- [Settings](settings.md) — configure execution timeouts and output limits

← [Back to Documentation Index](index.md)
