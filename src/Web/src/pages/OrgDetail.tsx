import { useEffect, useState } from "react";
import { useParams, Link, useNavigate } from "react-router-dom";
import {
  type Org, listMembers, addMember, updateMember, deleteMember,
  type OrgMember, getMe, deleteOrg
} from "../lib/orgs";
import { api } from "../lib/api";

export default function OrgDetail() {
  const { id } = useParams<{ id: string }>();
  const nav = useNavigate();

  const [org, setOrg] = useState<Org | null>(null);
  const [members, setMembers] = useState<OrgMember[]>([]);
  const [meSub, setMeSub] = useState<string | null>(null);
  const [isOwner, setIsOwner] = useState(false);

  const [form, setForm] = useState<{ userSub: string; userName: string; role: "Owner" | "Member" }>({
    userSub: "",
    userName: "",
    role: "Member",
  });

  async function load() {
    if (!id) return;
    const o = await api<Org>(`/organizations/${id}`);
    setOrg(o);

    const ms = await listMembers(id);
    setMembers(ms);

    const me = await getMe();
    const mySub = me.subRaw ?? null;
    setMeSub(mySub);

    const amOwner = !!mySub && ms.some(m => m.userSub === mySub && m.role === "Owner");
    setIsOwner(amOwner);
  }

  useEffect(() => {
    load().catch(console.error);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id]);

  async function onAdd(e: React.FormEvent) {
    e.preventDefault();
    if (!id) return;
    if (!form.userSub || !form.userName) return;
    await addMember(id, form);
    setForm({ userSub: "", userName: "", role: "Member" });
    await load();
  }

  async function onChangeRole(m: OrgMember, role: "Owner" | "Member") {
    if (!id) return;
    await updateMember(id, m.id, role);
    await load();
  }

  async function onRemove(m: OrgMember) {
    if (!id) return;
    if (!confirm(`Remove ${m.userName}?`)) return;
    await deleteMember(id, m.id);
    await load();
  }

  async function onDeleteOrg() {
    if (!id) return;
    if (!confirm("Delete this organization?")) return;
    await deleteOrg(id);
    nav("/");
  }

  const onRoleSelectChange = (e: React.ChangeEvent<HTMLSelectElement>) =>
    setForm((f) => ({ ...f, role: e.target.value as "Owner" | "Member" }));

  return (
    <div className="max-w-4xl mx-auto p-6 space-y-4">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">
          <Link className="underline" to="/">Organizations</Link>
          <span className="mx-2">/</span>
          {org?.name ?? "Loading..."}
        </h1>
        {isOwner && <button className="border rounded px-3 py-1" onClick={onDeleteOrg}>Delete Org</button>}
      </div>

      <section className="space-y-2">
        <h2 className="font-semibold">Members</h2>

        <ul className="space-y-2">
          {members.map(m => (
            <li key={m.id} className="border rounded px-3 py-2 flex items-center justify-between">
              <div>
                <div className="font-medium">{m.userName}</div>
                <div className="text-xs text-gray-500">{m.userSub}</div>
              </div>
              <div className="flex items-center gap-2">
                <select
                  className="border rounded px-2 py-1"
                  value={m.role}
                  onChange={e => onChangeRole(m, e.target.value as "Owner" | "Member")}
                  disabled={!isOwner}
                >
                  <option>Owner</option>
                  <option>Member</option>
                </select>
                <button className="border rounded px-3 py-1" onClick={() => onRemove(m)} disabled={!isOwner}>
                  Remove
                </button>
              </div>
            </li>
          ))}
          {!members.length && <li>No members.</li>}
        </ul>

        {isOwner ? (
          <form className="flex flex-wrap gap-2 items-center" onSubmit={onAdd}>
            <input
              className="border rounded px-3 py-2 flex-1 min-w-[240px]"
              placeholder="User sub (CIAM 'sub')"
              value={form.userSub}
              onChange={(e) => setForm(f => ({ ...f, userSub: e.target.value }))}
            />
            <input
              className="border rounded px-3 py-2 flex-1 min-w-[200px]"
              placeholder="User display name"
              value={form.userName}
              onChange={(e) => setForm(f => ({ ...f, userName: e.target.value }))}
            />
            <select
              className="border rounded px-2 py-2"
              value={form.role}
              onChange={onRoleSelectChange}
            >
              <option value="Member">Member</option>
              <option value="Owner">Owner</option>
            </select>
            <button className="bg-black text-white rounded px-4">Add member</button>
          </form>
        ) : (
          <div className="text-sm text-gray-600">Only Owners can manage members.</div>
        )}

        <div className="text-xs text-gray-500">
          Your sub: {meSub ?? "(unknown)"} — 테스트 시 상대 계정의 <code>sub</code> 값을 알고 있을 때 추가하세요.
        </div>
      </section>
    </div>
  );
}