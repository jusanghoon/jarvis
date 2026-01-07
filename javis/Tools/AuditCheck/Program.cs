using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

internal static class Program
{
    private const int EXIT_SCHEMA = 2;
    private const int EXIT_PARSE = 3;
    private const int EXIT_MISSING_END = 4;
    private const int EXIT_CUSTOM_LAT = 5;

    public static int Main(string[] args)
    {
        if (args.Length == 0 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Usage: audit-check <path-to-jsonl> [--warn-custom-avg <ms>] [--warn-custom-max <ms>] [--parse-error-samples <n>]");
            return EXIT_SCHEMA;
        }

        var path = args[0];

        double warnCustomAvgMs = 3000;
        long warnCustomMaxMs = 15000;
        int parseErrorSamples = 3;

        for (int i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (string.Equals(a, "--warn-custom-avg", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (double.TryParse(args[++i], out var v) && v >= 0) warnCustomAvgMs = v;
                continue;
            }

            if (string.Equals(a, "--warn-custom-max", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (long.TryParse(args[++i], out var v) && v >= 0) warnCustomMaxMs = v;
                continue;
            }

            if (string.Equals(a, "--parse-error-samples", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                if (int.TryParse(args[++i], out var v) && v >= 0) parseErrorSamples = v;
                continue;
            }
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return EXIT_PARSE;
        }

        if (!path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Not a .jsonl file: {path}");
            return EXIT_PARSE;
        }

        int parseErrors = 0;
        var parseErrorLines = new List<(string file, int line)>(capacity: Math.Max(0, parseErrorSamples));

        int archiveLines = 0;
        var metaKind = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var whereTop = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var modelCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var startOps = new HashSet<string>(StringComparer.Ordinal);
        var endOps = new HashSet<string>(StringComparer.Ordinal);

        int fossilizeCount = 0;
        int transitionCount = 0;

        var summaryEngineCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        long summaryMsTotal = 0;
        int summaryMsCount = 0;
        long summaryMsMax = 0;

        long customMsTotal = 0;
        int customMsCount = 0;
        long customMsMax = 0;

        long fossilSourceTotal = 0;
        int fossilSourceMax = 0;

        var fossilBuckets = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["1-20"] = 0,
            ["21-40"] = 0,
            ["41-80"] = 0,
            ["81+"] = 0
        };

        var fossilByHash = new HashSet<string>(StringComparer.Ordinal);
        var transitionByHash = new HashSet<string>(StringComparer.Ordinal);

        int missingTsUnixMs = 0;
        int missingEventId = 0;

        long sendMsTotal = 0;
        int sendMsCount = 0;
        long sendMsMax = 0;

        int lineNo = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNo++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument? doc = null;
            try
            {
                doc = JsonDocument.Parse(line);
            }
            catch
            {
                parseErrors++;
                if (parseErrorLines.Count < parseErrorSamples)
                    parseErrorLines.Add((Path.GetFileName(path), lineNo));
                continue;
            }

            using (doc)
            {
                var root = doc.RootElement;

                if (!root.TryGetProperty("kind", out var kindEl)) continue;
                if (!string.Equals(kindEl.GetString(), "archive", StringComparison.OrdinalIgnoreCase)) continue;

                if (!root.TryGetProperty("schema", out var schemaEl) ||
                    !string.Equals(schemaEl.GetString(), "jarvis.archive.v1", StringComparison.Ordinal))
                    continue;

                archiveLines++;

                if (!root.TryGetProperty("tsUnixMs", out var _)) missingTsUnixMs++;
                if (!root.TryGetProperty("eventId", out var _)) missingEventId++;

                if (root.TryGetProperty("meta", out var metaEl) && metaEl.ValueKind == JsonValueKind.Object)
                {
                    if (metaEl.TryGetProperty("model", out var modelEl))
                    {
                        var model = modelEl.GetString() ?? "unknown";
                        if (!string.IsNullOrWhiteSpace(model))
                            modelCount[model] = modelCount.TryGetValue(model, out var mc) ? mc + 1 : 1;
                    }

                    if (metaEl.TryGetProperty("kind", out var mkEl))
                    {
                        var mk = mkEl.GetString() ?? "unknown";
                        metaKind[mk] = metaKind.TryGetValue(mk, out var c) ? c + 1 : 1;

                        if (string.Equals(mk, "fossilize", StringComparison.Ordinal))
                        {
                            fossilizeCount++;

                            string? se = null;
                            if (metaEl.TryGetProperty("summaryEngine", out var seEl))
                            {
                                se = seEl.GetString();
                                var key = se ?? "unknown";
                                summaryEngineCount[key] = summaryEngineCount.TryGetValue(key, out var sec) ? sec + 1 : 1;
                            }

                            if (metaEl.TryGetProperty("summaryMs", out var smEl) && smEl.TryGetInt64(out var sm))
                            {
                                summaryMsTotal += sm;
                                summaryMsCount++;
                                summaryMsMax = Math.Max(summaryMsMax, sm);

                                if (!string.IsNullOrWhiteSpace(se) && se.Equals("custom", StringComparison.OrdinalIgnoreCase))
                                {
                                    customMsTotal += sm;
                                    customMsCount++;
                                    customMsMax = Math.Max(customMsMax, sm);
                                }
                            }

                            if (metaEl.TryGetProperty("sourceCount", out var scEl) && scEl.TryGetInt32(out var sc))
                            {
                                fossilSourceTotal += sc;
                                fossilSourceMax = Math.Max(fossilSourceMax, sc);

                                if (sc <= 20) fossilBuckets["1-20"]++;
                                else if (sc <= 40) fossilBuckets["21-40"]++;
                                else if (sc <= 80) fossilBuckets["41-80"]++;
                                else fossilBuckets["81+"]++;
                            }

                            if (metaEl.TryGetProperty("sourceIdsHash", out var hEl))
                            {
                                var h = hEl.GetString();
                                if (!string.IsNullOrWhiteSpace(h)) fossilByHash.Add(h);
                            }
                        }
                        else if (string.Equals(mk, "state.transition", StringComparison.Ordinal))
                        {
                            transitionCount++;

                            if (metaEl.TryGetProperty("sourceIdsHash", out var hEl))
                            {
                                var h = hEl.GetString();
                                if (!string.IsNullOrWhiteSpace(h)) transitionByHash.Add(h);
                            }
                        }

                        if (metaEl.TryGetProperty("opId", out var opEl))
                        {
                            var opId = opEl.GetString();
                            if (!string.IsNullOrWhiteSpace(opId))
                            {
                                if (string.Equals(mk, "chat.send.start", StringComparison.Ordinal)) startOps.Add(opId);
                                if (string.Equals(mk, "chat.send.end", StringComparison.Ordinal)) endOps.Add(opId);
                            }
                        }

                        if (string.Equals(mk, "chat.send.end", StringComparison.Ordinal))
                        {
                            if (metaEl.TryGetProperty("ms", out var msEl) && msEl.TryGetInt64(out var ms))
                            {
                                sendMsTotal += ms;
                                sendMsCount++;
                                sendMsMax = Math.Max(sendMsMax, ms);
                            }
                        }
                    }

                    if (metaEl.TryGetProperty("where", out var wEl))
                    {
                        var w = wEl.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(w))
                            whereTop[w] = whereTop.TryGetValue(w, out var wc) ? wc + 1 : 1;
                    }
                }
            }
        }

        Console.WriteLine($"archive(v1) lines: {archiveLines}");
        if (archiveLines > 200 && fossilizeCount == 0)
            Console.WriteLine("NOTE: many archive lines but no fossilize events (threshold not reached or buffering disabled).");

        Console.WriteLine($"parse errors: {parseErrors}");
        if (parseErrors > 0 && parseErrorLines.Count > 0)
            Console.WriteLine($"parse error samples: {string.Join(", ", parseErrorLines.Select(x => $"{x.file}:{x.line}"))}");

        Console.WriteLine("meta.kind top:");
        foreach (var kv in metaKind.OrderByDescending(x => x.Value).Take(20))
            Console.WriteLine($"  {kv.Key}: {kv.Value}");

        Console.WriteLine("meta.model top:");
        foreach (var kv in modelCount.OrderByDescending(x => x.Value).Take(10))
            Console.WriteLine($"  {kv.Key}: {kv.Value}");

        var missingEnd = startOps.Except(endOps).Take(50).ToList();
        var missingStart = endOps.Except(startOps).Take(50).ToList();
        Console.WriteLine($"start ops: {startOps.Count}, end ops: {endOps.Count}");
        Console.WriteLine($"missing end (top50): {missingEnd.Count}");
        Console.WriteLine($"missing start (top50): {missingStart.Count}");

        Console.WriteLine("where top:");
        foreach (var kv in whereTop.OrderByDescending(x => x.Value).Take(20))
            Console.WriteLine($"  {kv.Key}: {kv.Value}");

        Console.WriteLine();
        Console.WriteLine("schema completeness:");
        Console.WriteLine($"  missing tsUnixMs: {missingTsUnixMs}");
        Console.WriteLine($"  missing eventId : {missingEventId}");

        Console.WriteLine();
        Console.WriteLine("fossilize stats:");
        Console.WriteLine($"  fossilize events: {fossilizeCount}");
        Console.WriteLine($"  transition events: {transitionCount}");

        if (fossilizeCount > 0)
        {
            var avg = fossilSourceTotal / (double)fossilizeCount;
            Console.WriteLine($"  sourceCount total: {fossilSourceTotal}");
            Console.WriteLine($"  sourceCount avg  : {avg:F2}");
            Console.WriteLine($"  sourceCount max  : {fossilSourceMax}");

            Console.WriteLine("  sourceCount buckets:");
            foreach (var kv in fossilBuckets)
                Console.WriteLine($"    {kv.Key}: {kv.Value}");

            Console.WriteLine();
            Console.WriteLine("summaryEngine distribution:");
            foreach (var kv in summaryEngineCount.OrderByDescending(x => x.Value))
                Console.WriteLine($"  {kv.Key}: {kv.Value}");
        }

        Console.WriteLine();
        Console.WriteLine("summary latency:");
        if (summaryMsCount > 0)
        {
            Console.WriteLine($"  samples: {summaryMsCount}");
            Console.WriteLine($"  avg ms : {(summaryMsTotal / (double)summaryMsCount):F1}");
            Console.WriteLine($"  max ms : {summaryMsMax}");
        }
        else
        {
            Console.WriteLine("  no summaryMs found");
        }

        Console.WriteLine();
        Console.WriteLine("summary latency (custom only):");
        if (customMsCount > 0)
        {
            Console.WriteLine($"  samples: {customMsCount}");
            Console.WriteLine($"  avg ms : {(customMsTotal / (double)customMsCount):F1}");
            Console.WriteLine($"  max ms : {customMsMax}");
        }
        else
        {
            Console.WriteLine("  no custom summaryMs found");
        }

        Console.WriteLine();
        Console.WriteLine("warn thresholds:");
        Console.WriteLine($"  warnCustomAvgMs: {warnCustomAvgMs:F1}");
        Console.WriteLine($"  warnCustomMaxMs: {warnCustomMaxMs}");

        var missingTransition = fossilByHash.Except(transitionByHash).Count();
        var orphanTransition = transitionByHash.Except(fossilByHash).Count();

        Console.WriteLine();
        Console.WriteLine("fossilize/transition pairing (by sourceIdsHash):");
        Console.WriteLine($"  fossilize without transition: {missingTransition}");
        Console.WriteLine($"  transition without fossilize: {orphanTransition}");

        Console.WriteLine();
        Console.WriteLine("send latency:");
        if (sendMsCount > 0)
        {
            Console.WriteLine($"  end events with ms: {sendMsCount}");
            Console.WriteLine($"  avg ms: {(sendMsTotal / (double)sendMsCount):F1}");
            Console.WriteLine($"  max ms: {sendMsMax}");
        }
        else
        {
            Console.WriteLine("  no chat.send.end ms found");
        }

        Console.WriteLine();

        var warnSchema = missingEventId > 0 || missingTsUnixMs > 0;
        var warnParse = parseErrors > 0;
        var warnMissingEnd = missingEnd.Count > 0;

        var warnCustomLatency = false;
        double customAvgMs = 0;

        if (customMsCount > 0)
        {
            customAvgMs = customMsTotal / (double)customMsCount;
            warnCustomLatency = customAvgMs > warnCustomAvgMs || customMsMax > warnCustomMaxMs;
        }

        var hasWarning = warnSchema || warnParse || warnMissingEnd || warnCustomLatency;

        if (warnSchema)
            Console.WriteLine($"WARNING: schema completeness issues (missingEventId={missingEventId}, missingTsUnixMs={missingTsUnixMs})");

        if (warnParse)
            Console.WriteLine($"WARNING: JSONL parse errors (count={parseErrors})");

        if (warnMissingEnd)
            Console.WriteLine($"WARNING: chat.send.start without end detected (missingEnd={missingEnd.Count})");

        if (warnCustomLatency)
            Console.WriteLine($"WARNING: custom summary latency high (avgMs={customAvgMs:F1}, maxMs={customMsMax}, samples={customMsCount})");

        if (!hasWarning)
            Console.WriteLine("OK: no warnings.");

        // Exit code meaning (cause ID) + severity precedence:
        // 3=parse errors > 2=schema > 4=missing end > 5=custom latency
        int exitCode;
        if (!hasWarning) exitCode = 0;
        else if (warnParse) exitCode = EXIT_PARSE;
        else if (warnSchema) exitCode = EXIT_SCHEMA;
        else if (warnMissingEnd) exitCode = EXIT_MISSING_END;
        else if (warnCustomLatency) exitCode = EXIT_CUSTOM_LAT;
        else exitCode = 1;

        return exitCode;
    }
}
