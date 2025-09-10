import { useEffect, useMemo, useState } from "react";
import {
  searchOrgs, createOrg, updateOrg, deleteOrg,
  type Org, listMine
} from "../lib/orgs";
import { msalInstance, msalReady } from "../lib/msal";
import { Link } from "react-router-dom";

type Sort = "name" | "createdAt";
type Order = "asc" | "desc";

export default function Organizations() {
  const [items, setItems] = useState<Org[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(5);
  const [query, setQuery] = useState("");
  const [sort, setSort] = useState<Sort>("createdAt");
  const [order, setOrder] = useState<Order>("desc");

  const [name, setName] = useState("");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingName, setEditingName] = useState("");

  const [isAuthed, setIsAuthed] = useState(false);
  const [onlyMine, setOnlyMine] = useState(false);

  const maxPage = useMemo(
    () => Math.max(1, Math.ceil(total / pageSize)),
    [total, pageSize]
  );

  useEffect(() => {
    (async () => {
      await msalReady;
      setIsAuthed(!!msalInstance.getActiveAccount());
      const cb = msalInstance.addEventCallback(() => {
        setIsAuthed(!!msalInstance.getActiveAccount());
      });
      return () => { if (cb) msalInstance.removeEventCallback(cb); };
    })();
  }, []);

  async function load() {
    if (onlyMine) {
      const mine = await listMine();
      setItems(mine);
      setTotal(mine.length);
      setPage(1);
      return;
    }
    const res = await searchOrgs({ query, sort, order, page, pageSize });
    setItems(res.items);
    setTotal(res.total);
  }

  useEffect(() => {
    load().catch(console.error);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query, sort, order, page, pageSize, onlyMine]);

  async function onCreate(e: React.FormEvent) {
    e.preventDefault();
    if (!name.trim()) return;
    await createOrg(name.trim());      // ← 불필요 변수 제거
    setName("");
    await load();
  }

  async function onSave(id: string) {
    if (!editingName.trim()) return;
    await updateOrg(id, editingName.trim());
    setEditingId(null);
    setEditingName("");
    await load();
  }

  async function onDelete(id: string) {
    if (!confirm("Delete this organization?")) return;
    await deleteOrg(id);
    await load();
  }

  const onSortChange = (e: React.ChangeEvent<HTMLSelectElement>) =>
    setSort(e.target.value as Sort);

  const onOrderChange = (e: React.ChangeEvent<HTMLSelectElement>) =>
    setOrder(e.target.value as Order);

  const onPageSizeChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    setPage(1);
    setPageSize(Number(e.target.value));
  };

  const onOnlyMineChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    setOnlyMine(e.target.checked);
    setPage(1);
  };

  return (
    <div className="max-w-4xl mx-auto p-6 space-y-4">
      <h1 className="text-2xl font-bold">Organizations</h1>

      <div className="flex flex-wrap items-center gap-2">
        <input
          className="border rounded px-3 py-2"
          placeholder="Search by name"
          value={query}
          onChange={(e) => { setPage(1); setQuery(e.target.value); }}
        />
        <select className="border rounded px-2 py-2" value={sort} onChange={onSortChange}>
          <option value="createdAt">Sort: Created</option>
          <option value="name">Sort: Name</option>
        </select>
        <select className="border rounded px-2 py-2" value={order} onChange={onOrderChange}>
          <option value="desc">Desc</option>
          <option value="asc">Asc</option>
        </select>
        <select className="border rounded px-2 py-2" value={pageSize} onChange={onPageSizeChange}>
          <option value={5}>5 / page</option>
          <option value={10}>10 / page</option>
          <option value={20}>20 / page</option>
        </select>
        <label className="ml-auto text-sm flex items-center gap-2">
          <input type="checkbox" checked={onlyMine} onChange={onOnlyMineChange} />
          Only mine (Owner)
        </label>
      </div>

      {isAuthed ? (
        <form className="flex gap-2" onSubmit={onCreate}>
          <input
            className="border rounded px-3 py-2 flex-1"
            placeholder="New org name"
            value={name}
            onChange={(e) => setName(e.target.value)}
          />
          <button className="bg-black text-white rounded px-4">Add</button>
        </form>
      ) : (
        <div className="text-sm text-gray-600">Sign in to create / edit / delete.</div>
      )}

      <ul className="space-y-2">
        {items.map((o) => (
          <li key={o.id} className="border rounded px-3 py-2">
            {editingId === o.id ? (
              <div className="flex items-center gap-2">
                <input
                  className="border rounded px-2 py-1 flex-1"
                  value={editingName}
                  onChange={(e) => setEditingName(e.target.value)}
                />
                <button className="px-3 py-1 rounded bg-black text-white" onClick={() => onSave(o.id)}>
                  Save
                </button>
                <button
                  className="px-3 py-1 rounded border"
                  onClick={() => { setEditingId(null); setEditingName(""); }}
                >
                  Cancel
                </button>
              </div>
            ) : (
              <div className="flex items-start justify-between gap-3">
                <div>
                  <div className="font-medium">
                    <Link className="underline" to={`/orgs/${o.id}`}>{o.name}</Link>
                  </div>
                  <div className="text-xs text-gray-500">
                    {new Date(o.createdAt).toLocaleString()}
                    {o.createdByName ? ` • by ${o.createdByName}` : ""}
                  </div>
                </div>
                {isAuthed && (
                  <div className="flex gap-2 shrink-0">
                    <button
                      className="px-3 py-1 rounded border"
                      onClick={() => { setEditingId(o.id); setEditingName(o.name); }}
                    >
                      Edit
                    </button>
                    <button className="px-3 py-1 rounded border" onClick={() => onDelete(o.id)}>
                      Delete
                    </button>
                  </div>
                )}
              </div>
            )}
          </li>
        ))}
        {!items.length && <li>No organizations.</li>}
      </ul>

      {!onlyMine && (
        <div className="flex items-center gap-2 justify-end">
          <button
            disabled={page <= 1}
            className="border rounded px-3 py-1"
            onClick={() => setPage((p) => Math.max(1, p - 1))}
          >
            Prev
          </button>
          <div className="text-sm">Page {page} / {maxPage}</div>
          <button
            disabled={page >= maxPage}
            className="border rounded px-3 py-1"
            onClick={() => setPage((p) => Math.min(maxPage, p + 1))}
          >
            Next
          </button>
        </div>
      )}
    </div>
  );
}