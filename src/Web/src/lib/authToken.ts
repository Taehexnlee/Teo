// src/Web/src/lib/authToken.ts
import { InteractionRequiredAuthError } from "@azure/msal-browser";
import { msalInstance } from "./msal";
import { API_SCOPES } from "./msalScopes";

export async function getAccessToken(): Promise<string | null> {
  const accounts = msalInstance.getAllAccounts();
  if (accounts.length === 0) return null;

  try {
    const res = await msalInstance.acquireTokenSilent({
      account: accounts[0],
      scopes: API_SCOPES
    });
    return res.accessToken;
  } catch (e) {
    if (e instanceof InteractionRequiredAuthError) {
      const res = await msalInstance.acquireTokenPopup({ scopes: API_SCOPES });
      return res.accessToken;
    }
    throw e;
  }
}