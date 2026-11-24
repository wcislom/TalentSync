import { readFile } from 'node:fs/promises';
import { createAppAuth } from '@octokit/auth-app';

// The GitHub App the agent pushes and opens PRs as, so a PR is not the reviewer's own and can be approved by them.
export const appVariableNames = {
  appId: 'AGENT_APP_ID',
  installationId: 'AGENT_APP_INSTALLATION_ID',
  privateKeyPath: 'AGENT_APP_PRIVATE_KEY_PATH',
} as const;

export type AppCredentials = { appId: number; installationId: number; privateKeyPath: string };

type Environment = Record<string, string | undefined>;

// Errors name the variable, never its value.
export function readAppCredentials(environment: Environment): AppCredentials {
  const read = (name: string): string => {
    const value = environment[name]?.trim();
    if (!value) throw new Error(`the environment variable ${name} is not set`);
    return value;
  };
  const readId = (name: string): number => {
    const value = read(name);
    if (!/^[1-9][0-9]*$/.test(value)) throw new Error(`the environment variable ${name} is not a number`);
    return Number(value);
  };
  return {
    appId: readId(appVariableNames.appId),
    installationId: readId(appVariableNames.installationId),
    privateKeyPath: read(appVariableNames.privateKeyPath),
  };
}

// The token is valid for an hour and only for the repositories the App is installed on. It is passed to one
// git or gh process at a time through its environment, never to the skill's session.
export async function getInstallationToken(): Promise<string> {
  const credentials = readAppCredentials(process.env);
  let privateKey: string;
  try {
    privateKey = await readFile(credentials.privateKeyPath, 'utf8');
  } catch {
    throw new Error(`could not read the private key named by ${appVariableNames.privateKeyPath}`);
  }
  // The PKCS#1 key GitHub generates is converted to PKCS#8 by the library on Node.
  const auth = createAppAuth({ appId: credentials.appId, privateKey });
  const authentication = await auth({ type: 'installation', installationId: credentials.installationId });
  return authentication.token;
}
