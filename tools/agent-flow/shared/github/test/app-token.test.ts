import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readAppCredentials } from '../app-token.ts';

const environment = {
  AGENT_APP_ID: '5',
  AGENT_APP_INSTALLATION_ID: '7',
  AGENT_APP_PRIVATE_KEY_PATH: 'key.pem',
};

test('the three variables give the credentials', () => {
  assert.deepEqual(readAppCredentials(environment), { appId: 5, installationId: 7, privateKeyPath: 'key.pem' });
});

test('a missing variable is named in the error', () => {
  assert.throws(() => readAppCredentials({ ...environment, AGENT_APP_INSTALLATION_ID: ' ' }),
    /AGENT_APP_INSTALLATION_ID is not set/);
});

test('an id that is not a number is refused without its value in the error', () => {
  assert.throws(() => readAppCredentials({ ...environment, AGENT_APP_ID: 'Iv23secret' }),
    (error: Error) => /AGENT_APP_ID is not a number/.test(error.message) && !error.message.includes('Iv23secret'));
});
