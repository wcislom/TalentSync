import { Component, inject } from '@angular/core';
import { RUNTIME_CONFIG } from '../../core/runtime-config/runtime-config';
import { SCENARIO_CATALOG } from '../catalog/scenario-catalog';
import { ScenarioCard } from '../scenario-card/scenario-card';

@Component({
  selector: 'app-scenarios-page',
  imports: [ScenarioCard],
  templateUrl: './scenarios-page.html',
  styleUrl: './scenarios-page.scss',
})
export class ScenariosPage {
  protected readonly scenarios = SCENARIO_CATALOG;
  protected readonly tracesUrl = inject(RUNTIME_CONFIG).tracesUrl;
}
