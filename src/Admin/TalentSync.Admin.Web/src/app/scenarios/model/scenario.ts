import { SimulatorsClient } from '../data-access/simulators-client';
import { ResultCheck } from './result-check';

/** Runs one step and records its outcome on the page; a failed step throws and stops the scenario. */
export type Step = <T>(label: string, action: () => Promise<T>) => Promise<T>;

export interface Scenario {
  /** Prefix of the candidate id; every run gets a new id, because simulator state is never reset. */
  key: string;
  title: string;
  description: string;
  expectation: string;
  run(simulators: SimulatorsClient, candidateId: string, step: Step): Promise<void>;
  checks: ResultCheck[];
}
