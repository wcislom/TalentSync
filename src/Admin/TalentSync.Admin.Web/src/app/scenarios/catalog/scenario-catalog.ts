import { Scenario } from '../model/scenario';
import { crmDownScenario } from './crm-down.scenario';
import { noCvScenario } from './no-cv.scenario';
import { poisonScenario } from './poison.scenario';
import { successScenario } from './success.scenario';

/** In page order. */
export const SCENARIO_CATALOG: readonly Scenario[] = [successScenario, crmDownScenario, noCvScenario, poisonScenario];
