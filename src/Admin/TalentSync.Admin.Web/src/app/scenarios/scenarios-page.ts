import { Component } from '@angular/core';
import { ScenarioCard } from './scenario-card';
import { SCENARIOS } from './scenarios';

@Component({
  selector: 'app-scenarios-page',
  imports: [ScenarioCard],
  template: `
    <h1>Scenarios</h1>
    <p>
      Each run uses a new candidate id. A webhook a step triggers is one trace in the Aspire Dashboard
      (<a href="http://localhost:18888" target="_blank" rel="noopener">localhost:18888</a>).
    </p>
    <div class="grid">
      @for (scenario of scenarios; track scenario.key) {
        <app-scenario-card [scenario]="scenario" />
      }
    </div>
  `,
  styles: `
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(480px, 1fr));
      gap: 16px;
    }
  `,
})
export class ScenariosPage {
  protected readonly scenarios = SCENARIOS;
}
