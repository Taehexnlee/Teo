import { useCallback, useEffect, useState, type FormEvent } from "react";
import { createOrg, listOrgs, type Org } from "../../lib/orgs";

export default function Organizations() {
  const [orgs, setOrgs] = useState<Org[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState("");

  const load = useCallback(async () => {
    try {
      setLoading(true);
      setError(null);
      const data = await listOrgs();
      setOrgs(data);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : "Failed to load";
      setError(msg);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const onCreate = async (e: FormEvent) => {
    e.preventDefault();
    if (!name.trim()) return;
    try {
      await createOrg(name.trim());
      setName("");
      await load();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : "Failed to create";
      alert(msg);
    }
  };

  return (
    <div className="max-w-2xl mx-auto p-6 space-y-6">
      <h1 className="text-2xl font-bold">Organizations</h1>

      <form onSubmit={onCreate} className="flex gap-2">
        <input
          className="flex-1 border rounded px-3 py-2"
          placeholder="Organization name"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />
        <button
          className="px-4 py-2 rounded bg-black text-white"
          type="submit"
        >
          Add
        </button>
      </form>

      {loading && <p className="text-sm text-gray-500">Loading...</p>}
      {error && <p className="text-sm text-red-600">{error}</p>}

      <ul className="divide-y border rounded">
        {orgs.map((o) => (
          <li key={o.id} className="p-3">
            <div className="font-medium">{o.name}</div>
            <div className="text-xs text-gray-500">{new Date(o.createdAt).toLocaleString()}</div>
          </li>
        ))}
        {!loading && !error && orgs.length === 0 && (
          <li className="p-3 text-sm text-gray-500">No organizations yet.</li>
        )}
      </ul>
    </div>
  );
}
