import { Component, input, computed } from '@angular/core';
import { BaseChartDirective } from 'ng2-charts';
import { ChartData, ChartOptions } from 'chart.js';
import { TopCompany } from '../../../../core/models/stats.model';

@Component({
  selector: 'app-top-companies-chart',
  standalone: true,
  imports: [BaseChartDirective],
  template: `
    <canvas baseChart
      [data]="chartData()"
      [options]="options"
      type="bar">
    </canvas>
  `
})
export class TopCompaniesChartComponent {
  data = input<TopCompany[]>([]);

  options: ChartOptions<'bar'> = {
    responsive: true,
    indexAxis: 'y',
    plugins: { legend: { display: false } },
    scales: { x: { beginAtZero: true } }
  };

  chartData = computed<ChartData<'bar'>>(() => ({
    labels: this.data().map(c => c.name),
    datasets: [{
      data: this.data().map(c => c.jobCount),
      backgroundColor: 'rgba(63, 81, 181, 0.7)',
      borderColor: 'rgba(63, 81, 181, 1)',
      borderWidth: 1
    }]
  }));
}
