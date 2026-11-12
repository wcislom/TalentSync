import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { APP_CONFIG } from '../config';

export interface NewCandidate {
  firstName: string;
  lastName: string;
  email: string;
  phone?: string;
  resumeText: string;
}

export interface AnomalyRule {
  mode: string;
  candidateId: string;
  seconds: number;
}

/** Drives the three simulators. Responses stay untyped except the ids a later step needs. */
@Injectable({ providedIn: 'root' })
export class Simulators {
  private readonly http = inject(HttpClient);
  private readonly config = inject(APP_CONFIG);

  apply(jobId: string, candidateId: string, candidate: NewCandidate): Promise<{ jobApplicationId: string }> {
    return firstValueFrom(
      this.http.post<{ jobApplicationId: string }>(`${this.ats}/recruiter/jobs/${jobId}/apply`, {
        candidateId,
        ...candidate,
      }),
    );
  }

  changeStage(jobApplicationId: string, stage: string): Promise<unknown> {
    return firstValueFrom(
      this.http.patch(`${this.ats}/recruiter/job-applications/${jobApplicationId}/stage`, { stage }),
    );
  }

  updateCandidate(candidateId: string, changes: Partial<NewCandidate>): Promise<unknown> {
    return firstValueFrom(this.http.patch(`${this.ats}/recruiter/candidates/${candidateId}`, changes));
  }

  breakAts(rule: AnomalyRule): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.ats}/anomalies`, rule));
  }

  breakCrm(rule: AnomalyRule): Promise<unknown> {
    return firstValueFrom(this.http.post(`${this.config.crmSimulatorUrl}/anomalies`, rule));
  }

  atsCallsUrl(candidateId: string): string {
    return `${this.ats}/calls?candidateId=${encodeURIComponent(candidateId)}`;
  }

  crmCallsUrl(candidateId: string): string {
    return `${this.config.crmSimulatorUrl}/calls?candidateId=${encodeURIComponent(candidateId)}`;
  }

  crmContactsUrl(): string {
    return `${this.config.crmSimulatorUrl}/contacts`;
  }

  profileUrl(candidateId: string): string {
    return `${this.config.matchingPlatformSimulatorUrl}/profiles/${encodeURIComponent(candidateId)}`;
  }

  private get ats(): string {
    return this.config.atsSimulatorUrl;
  }
}
