import { api } from "./api";

export type Org = {
  id: string;
  name: string;
  createdAt: string;
  createdBy?: string | null;
  createdByName?: string | null;
};

export type OrgMember = {
  id: string;
  orgId: string;
  userSub: string;
  userName: string;
  role: "Owner" | "Member";
  createdAt: string;
};

export type Paged<T> = {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
};

// 기본 목록 (기존 호환)
export const listOrgs = () => api<Org[]>("/organizations");

// 검색/정렬/페이지
export const searchOrgs = (params: {
  query?: string;
  sort?: "name" | "createdAt";
  order?: "asc" | "desc";
  page?: number;
  pageSize?: number;
}) => {
  const u = new URLSearchParams();
  if (params.query) u.set("query", params.query);
  if (params.sort)  u.set("sort", params.sort);
  if (params.order) u.set("order", params.order);
  if (params.page)  u.set("page", String(params.page));
  if (params.pageSize) u.set("pageSize", String(params.pageSize));
  return api<Paged<Org>>(`/organizations/search?${u.toString()}`);
};

export const createOrg  = (name: string) => api<Org>  ("/organizations", { method:"POST", body: JSON.stringify({ name }) });
export const updateOrg  = (id: string, name: string) => api<Org>(`/organizations/${id}`, { method:"PUT", body: JSON.stringify({ name }) });
export const deleteOrg  = (id: string) => api<void>(`/organizations/${id}`, { method:"DELETE" });
export const listMine   = () => api<Org[]>("/organizations/mine");

// 멤버
export const listMembers  = (orgId: string) => api<OrgMember[]>(`/organizations/${orgId}/members`);
export const addMember    = (orgId: string, p: { userSub: string; userName: string; role: "Owner" | "Member" }) =>
  api<OrgMember>(`/organizations/${orgId}/members`, { method:"POST", body: JSON.stringify(p) });
export const updateMember = (orgId: string, memberId: string, role: "Owner" | "Member") =>
  api<void>(`/organizations/${orgId}/members/${memberId}`, { method:"PUT", body: JSON.stringify({ role }) });
export const deleteMember = (orgId: string, memberId: string) =>
  api<void>(`/organizations/${orgId}/members/${memberId}`, { method:"DELETE" });

// /me (내 sub/name 확인용)
export type Me = { name?: string; oidRaw?: string|null; subRaw?: string|null };
export const getMe = () => api<Me>("/me");