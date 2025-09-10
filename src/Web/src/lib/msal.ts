// src/Web/src/lib/msal.ts
import {
  PublicClientApplication,
  InteractionRequiredAuthError,
  LogLevel,
  type AccountInfo,
} from "@azure/msal-browser";

// ── Env 읽기 + 정규화
const RAW_INSTANCE = (import.meta.env.VITE_ADB2C_INSTANCE ?? "").trim();
const TENANT_ID   = (import.meta.env.VITE_ADB2C_TENANT_ID ?? "").trim();
const CLIENT_ID   = (import.meta.env.VITE_ADB2C_CLIENT_ID ?? "").trim();
const API_SCOPE   = (import.meta.env.VITE_API_SCOPE ?? "").trim();

const INSTANCE  = RAW_INSTANCE.replace(/\/+$/g, ""); // 끝 슬래시 제거
const AUTHORITY = `${INSTANCE}/${TENANT_ID}`;

// ── 빠른 유효성 체크(경고만)
(function assertEnv() {
  const errs: string[] = [];
  if (!/^https:\/\/[^/]+\.ciamlogin\.com$/i.test(INSTANCE)) {
    errs.push(`VITE_ADB2C_INSTANCE 형식이 이상함: "${RAW_INSTANCE}" (예: https://<tenant>.ciamlogin.com)`);
  }
  if (!/^[0-9a-f-]{36}$/i.test(TENANT_ID)) {
    errs.push(`VITE_ADB2C_TENANT_ID 형식이 GUID가 아님: "${TENANT_ID}"`);
  }
  if (!CLIENT_ID) errs.push("VITE_ADB2C_CLIENT_ID 누락");
  if (!API_SCOPE) errs.push("VITE_API_SCOPE 누락");
  if (errs.length) console.warn("[msal] ENV WARNING:\n- " + errs.join("\n- "));
})();

// ✅ 로그 레벨: 개발에선 Warning, 프로덕션은 Error만
const LOG_LEVEL = import.meta.env.DEV ? LogLevel.Warning : LogLevel.Error;

export const msalConfig = {
  auth: {
    clientId: CLIENT_ID,
    authority: AUTHORITY,                       // https://<tenant>.ciamlogin.com/<tenantId>
    knownAuthorities: [new URL(INSTANCE).host], // 권한 검증 호스트
    redirectUri: window.location.origin,
    postLogoutRedirectUri: window.location.origin,
    // 필요시 discovery 우회:
    // authorityMetadata: JSON.stringify({
    //   issuer: `${INSTANCE}/${TENANT_ID}/v2.0`,
    //   authorization_endpoint: `${INSTANCE}/${TENANT_ID}/oauth2/v2.0/authorize`,
    //   token_endpoint: `${INSTANCE}/${TENANT_ID}/oauth2/v2.0/token`,
    //   end_session_endpoint: `${INSTANCE}/${TENANT_ID}/oauth2/v2.0/logout`,
    //   jwks_uri: `${INSTANCE}/${TENANT_ID}/discovery/v2.0/keys`,
    // }),
  },
  cache: {
    cacheLocation: "localStorage",
    storeAuthStateInCookie: false,
  },
  system: {
    loggerOptions: {
      logLevel: LOG_LEVEL,
      loggerCallback: (level: LogLevel, message: string, containsPii: boolean) => {
        if (containsPii) return;
        if (level === LogLevel.Error)   console.error("[msal]", message);
        else if (level === LogLevel.Warning) console.warn("[msal]", message);
        // Info/Debug는 현재 레벨에서 발생하지 않음
      },
    },
  },
};

export const msalInstance = new PublicClientApplication(msalConfig);

export const msalReady = (async () => {
  await msalInstance.initialize();
  const accounts = msalInstance.getAllAccounts();
  if (accounts.length) msalInstance.setActiveAccount(accounts[0]);
  console.log("[msal] ready. accounts:", accounts);
})();

export async function login() {
  await msalReady;
  const existing = msalInstance.getAllAccounts();
  if (existing.length) {
    msalInstance.setActiveAccount(existing[0]);
    return existing[0];
  }
  const res = await msalInstance.loginPopup({ scopes: [API_SCOPE] });
  if (res.account) msalInstance.setActiveAccount(res.account);
  return res.account!;
}

export async function logout() {
  await msalReady;
  const account = msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0];
  await msalInstance.logoutPopup({ account });
}

export async function getToken(): Promise<string> {
  await msalReady;
  let account: AccountInfo | null =
    msalInstance.getActiveAccount() ?? msalInstance.getAllAccounts()[0] ?? null;
  if (!account) account = (await login()) as AccountInfo;

  try {
    const res = await msalInstance.acquireTokenSilent({ account, scopes: [API_SCOPE] });
    return res.accessToken;
  } catch (e) {
    if (e instanceof InteractionRequiredAuthError) {
      const res = await msalInstance.acquireTokenPopup({ scopes: [API_SCOPE] });
      return res.accessToken;
    }
    console.error("[msal] token error", e);
    throw e;
  }
}

export async function callMe() {
  const token = await getToken();
  const base = (import.meta.env.VITE_API_BASE ?? "").replace(/\/+$/g, "");
  const r = await fetch(`${base}/me`, { headers: { Authorization: `Bearer ${token}` } });
  const body = await r.json().catch(() => ({}));
  return { status: r.status, body };
}

export async function getMyOid(): Promise<string | null> {
  const token = await getToken();
  const payload = JSON.parse(atob(token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/")));
  return payload.sub ?? payload.oid ?? null;
}

// ✅ 콘솔 편의용 전역 바인딩
// eslint-disable-next-line @typescript-eslint/no-explicit-any
;(window as any).msal = {
  config: msalConfig,
  instance: msalInstance,
  ready: msalReady,
  login,
  logout,
  getToken,
  callMe,
  getMyOid,
};