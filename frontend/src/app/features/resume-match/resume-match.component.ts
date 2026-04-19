import { Component, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { DatePipe, NgFor, NgIf, PercentPipe, TitleCasePipe } from '@angular/common';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatChipsModule } from '@angular/material/chips';
import { ResumeJobMatch, ResumeResult } from '../../core/models/resume.model';
import { ResumeService } from '../../core/services/resume.service';
import { LocationFilterService } from '../../core/services/location-filter.service';

type Stage = 'idle' | 'uploading' | 'fetching' | 'done' | 'error';

const STORAGE_KEY = 'resume_match_results';

interface StoredResults {
  resume: ResumeResult;
  matches: ResumeJobMatch[];
  savedAt: string;
}

@Component({
  selector: 'app-resume-match',
  standalone: true,
  imports: [
    NgIf, NgFor, PercentPipe, TitleCasePipe, DatePipe,
    MatCardModule, MatButtonModule, MatIconModule,
    MatProgressBarModule, MatChipsModule,
  ],
  templateUrl: './resume-match.component.html',
  styleUrl: './resume-match.component.scss'
})
export class ResumeMatchComponent implements OnInit, OnDestroy {
  private resumeService = inject(ResumeService);
  private locationFilter = inject(LocationFilterService);

  stage = signal<Stage>('idle');
  error = signal<string | null>(null);
  selectedFile = signal<File | null>(null);
  resume = signal<ResumeResult | null>(null);
  matches = signal<ResumeJobMatch[]>([]);
  isDragging = signal(false);
  progress = signal(0);
  progressLabel = signal('');
  savedAt = signal<string | null>(null);

  private progressTimer?: ReturnType<typeof setInterval>;

  ngOnInit(): void {
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) {
        const stored: StoredResults = JSON.parse(raw);
        this.resume.set(stored.resume);
        this.matches.set(stored.matches);
        this.savedAt.set(stored.savedAt);
        this.stage.set('done');
      }
    } catch {
      localStorage.removeItem(STORAGE_KEY);
    }
  }

  ngOnDestroy(): void {
    this.clearTimer();
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) this.setFile(file);
    input.value = '';
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.isDragging.set(true);
  }

  onDragLeave(): void {
    this.isDragging.set(false);
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragging.set(false);
    const file = event.dataTransfer?.files[0];
    if (file) this.setFile(file);
  }

  private setFile(file: File): void {
    const ext = file.name.split('.').pop()?.toLowerCase();
    if (!['pdf', 'docx', 'txt'].includes(ext ?? '')) {
      this.error.set('Only PDF, DOCX, and TXT files are supported.');
      return;
    }
    this.selectedFile.set(file);
    this.stage.set('idle');
    this.error.set(null);
    this.resume.set(null);
    this.matches.set([]);
    this.progress.set(0);
  }

  analyze(): void {
    const file = this.selectedFile();
    if (!file) return;

    this.stage.set('uploading');
    this.error.set(null);

    // Phase 1: upload + Claude extraction + embedding (~10s) → animate 0→48%
    this.animateTo(0, 48, 10_000, 'Analyzing resume with AI…');

    this.resumeService.uploadResume(file).subscribe({
      next: result => {
        this.resume.set(result);
        this.stage.set('fetching');

        // Phase 2: vector search (~4s) → animate 50→90%
        this.progress.set(50);
        this.animateTo(50, 90, 4_000, 'Finding matching jobs…');

        const isUs = this.locationFilter.usOnly() ? true : undefined;
        this.resumeService.getMatches(result.id, 20, isUs).subscribe({
          next: matchResult => {
            this.clearTimer();
            this.progress.set(100);
            this.progressLabel.set('Complete!');
            this.matches.set(matchResult.matches);
            this.stage.set('done');
            const savedAt = new Date().toISOString();
            this.savedAt.set(savedAt);
            try {
              localStorage.setItem(STORAGE_KEY, JSON.stringify({
                resume: result,
                matches: matchResult.matches,
                savedAt,
              } satisfies StoredResults));
            } catch { /* storage full — ignore */ }
          },
          error: () => {
            this.clearTimer();
            this.error.set('Failed to retrieve job matches.');
            this.stage.set('error');
          }
        });
      },
      error: (err) => {
        this.clearTimer();
        const msg = err?.error?.error ?? 'Failed to process resume. Please try again.';
        this.error.set(msg);
        this.stage.set('error');
      }
    });
  }

  reset(): void {
    this.clearTimer();
    this.stage.set('idle');
    this.selectedFile.set(null);
    this.resume.set(null);
    this.matches.set([]);
    this.error.set(null);
    this.progress.set(0);
    this.progressLabel.set('');
    this.savedAt.set(null);
    localStorage.removeItem(STORAGE_KEY);
  }

  private animateTo(from: number, to: number, durationMs: number, label: string): void {
    this.clearTimer();
    this.progressLabel.set(label);
    this.progress.set(from);

    const intervalMs = 200;
    const steps = durationMs / intervalMs;
    const stepSize = (to - from) / steps;
    let current = from;

    this.progressTimer = setInterval(() => {
      current = Math.min(current + stepSize, to);
      this.progress.set(Math.round(current));
      if (current >= to) this.clearTimer();
    }, intervalMs);
  }

  private clearTimer(): void {
    if (this.progressTimer !== undefined) {
      clearInterval(this.progressTimer);
      this.progressTimer = undefined;
    }
  }

  similarityLabel(score: number): string {
    if (score >= 0.70) return 'Excellent match';
    if (score >= 0.65) return 'Strong match';
    if (score >= 0.60) return 'Good match';
    return 'Partial match';
  }

  similarityClass(score: number): string {
    if (score >= 0.70) return 'match-excellent';
    if (score >= 0.65) return 'match-strong';
    if (score >= 0.60) return 'match-good';
    return 'match-partial';
  }

  formatFileSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
