export default function Home() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-zinc-950 px-6 py-12 text-zinc-100">
      <main className="w-full max-w-3xl rounded-2xl border border-zinc-800 bg-zinc-900 p-8 shadow-xl">
        <p className="text-sm uppercase tracking-[0.25em] text-zinc-400">Glitch API</p>
        <h1 className="mt-2 text-3xl font-semibold tracking-tight">Operational Endpoints</h1>
        <p className="mt-4 text-zinc-300">
          This project serves backend endpoints for licensing, webhook ingestion, and
          entitlement services.
        </p>

        <div className="mt-8 grid gap-3">
          <EndpointRow method="GET" path="/api/health" description="Service heartbeat" />
          <EndpointRow
            method="POST"
            path="/api/webhooks/whop"
            description="Whop webhook entrypoint with signature verification scaffold"
          />
          <EndpointRow
            method="POST"
            path="/api/license/validate"
            description="License validation contract with plan, feature flags, and limits"
          />
          <EndpointRow
            method="POST"
            path="/api/license/heartbeat"
            description="Heartbeat contract with policy refresh and grace metadata"
          />
          <EndpointRow
            method="GET"
            path="/api/admin/dashboard/overview"
            description="Admin snapshot for webhook events, attribution, bindings, and policy status"
          />
        </div>
      </main>
    </div>
  );
}

function EndpointRow(props: { method: string; path: string; description: string }) {
  return (
    <div className="rounded-xl border border-zinc-800 bg-zinc-950 p-4">
      <div className="flex items-center gap-3">
        <span className="rounded-md bg-zinc-800 px-2 py-1 font-mono text-xs text-zinc-200">
          {props.method}
        </span>
        <code className="text-sm text-zinc-100">{props.path}</code>
      </div>
      <p className="mt-2 text-sm text-zinc-400">{props.description}</p>
    </div>
  );
}
