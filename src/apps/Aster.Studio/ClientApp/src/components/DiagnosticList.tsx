import type { CodedDiagnostic } from "@/lib/api";

/** SDK diagnostics with their stable codes first, so hosts and tests can key off the code rather than the text. */
export function DiagnosticList({ diagnostics }: { diagnostics: CodedDiagnostic[] }) {
  if (!diagnostics.length) return null;
  return (
    <ul aria-label="Diagnostics" className="space-y-1.5 rounded-md border border-amber-400/30 bg-amber-400/10 p-3 text-sm">
      {diagnostics.map((diagnostic, index) => <li key={`${diagnostic.code}-${index}`} className="flex flex-wrap items-baseline gap-2"><code className="rounded bg-background/60 px-1.5 py-0.5 font-mono text-xs text-amber-200">{diagnostic.code}</code><span className="text-muted-foreground">{diagnostic.message}</span></li>)}
    </ul>
  );
}
