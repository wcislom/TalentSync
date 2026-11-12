import { SimulatorsClient } from '../data-access/simulators-client';

/** A button on a scenario card that reads what a run produced: a CRM contact, a profile, call counts. */
export interface ResultCheck {
  label: string;
  url(simulators: SimulatorsClient, candidateId: string): string;
  /** Narrows a list response to the scenario's candidate. */
  select?(body: unknown, candidateId: string): unknown;
}
