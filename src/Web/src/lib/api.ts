// src/Web/src/lib/api.ts
import { getToken } from "./msal";

const RAW_BASE = import.meta.env.VITE_API_BASE as string | undefined;
if (!RAW_BASE) {
  throw new Error("VITE_API_BASE is not set");
}
const API_BASE = RAW_BASE.replace(/\/+$/, ""); // 끝 슬래시 제거

// RFC7807 ProblemDetails 형태(백엔드의 Results.Problem 등)
type ProblemDetails = {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  instance?: string;
  [key: string]: unknown;
};

class HttpError extends Error {
  status: number;
  problem?: ProblemDetails;
  constructor(status: number, message: string, problem?: ProblemDetails) {
    super(message);
    this.status = status;
    this.problem = problem;
  }
}

/**
 * 인증 토큰을 자동 주입하는 fetch 래퍼
 * - 401이면 토큰 갱신 후 1회 재시도
 * - 경로는 항상 '/api/...' 로 넘겨주세요
 */
export async function api<T = unknown>(
  path: string,
  init: RequestInit = {}
): Promise<T> {
  const token = await getToken();

  const headers = new Headers(init.headers || {});
  if (!headers.has("Authorization")) headers.set("Authorization", `Bearer ${token}`);
  if (init.body && !headers.has("Content-Type")) headers.set("Content-Type", "application/json");

  const url = `${API_BASE}${path.startsWith("/") ? path : `/${path}`}`;

  let res = await fetch(url, { ...init, headers });

  // 401이면 한 번만 재시도
  if (res.status === 401) {
    const fresh = await getToken();
    headers.set("Authorization", `Bearer ${fresh}`);
    res = await fetch(url, { ...init, headers });
  }

  // 본문 안전 파싱
  const text = await res.text();
  const body: unknown = text ? JSON.parse(text) : undefined;

  if (!res.ok) {
    const problem = body as ProblemDetails | undefined;
    const msg = problem?.detail || problem?.title || res.statusText || `HTTP ${res.status}`;
    throw new HttpError(res.status, msg, problem);
  }

  return body as T;
}