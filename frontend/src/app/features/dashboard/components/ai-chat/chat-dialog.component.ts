import { Component } from '@angular/core';
import { MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { AiChatComponent } from './ai-chat.component';

@Component({
  selector: 'app-chat-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule, AiChatComponent],
  template: `
    <div class="dialog-header">
      <span>AI Insights</span>
      <button mat-icon-button mat-dialog-close><mat-icon>close</mat-icon></button>
    </div>
    <mat-dialog-content>
      <app-ai-chat />
    </mat-dialog-content>
  `,
  styles: [`
    .dialog-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 8px 8px 8px 20px;
      font-size: 16px;
      font-weight: 500;
      border-bottom: 1px solid #e0e0e0;
    }
    mat-dialog-content {
      height: 65vh;
      max-height: 65vh;
      padding: 0 !important;
      overflow: hidden;
      display: flex;
      flex-direction: column;
      min-height: 0;
    }
  `]
})
export class ChatDialogComponent {}
