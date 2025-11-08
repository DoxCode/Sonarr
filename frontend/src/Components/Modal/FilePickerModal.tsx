import React, { useCallback, useEffect, useState } from 'react';
import Button from 'Components/Link/Button';
import Icon from 'Components/Icon';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import styles from './FilePickerModal.css';

interface FileEntry {
  path: string;
  name: string;
  isDirectory: boolean;
  isParent: boolean;
  size?: number;
}

interface FilePickerModalProps {
  seriesId: number;
  seriesPath: string;
  onSelect: (filePath: string) => void;
  onClose: () => void;
}

function FilePickerModal(props: FilePickerModalProps) {
  const { seriesId, seriesPath, onSelect, onClose } = props;
  const [currentPath, setCurrentPath] = useState(seriesPath);
  const [files, setFiles] = useState<FileEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadFiles = useCallback(async (path: string) => {
    setLoading(true);
    setError(null);
    try {
      const encodedPath = encodeURIComponent(path);
      const promise = createAjaxRequest({
        url: `/filesystem/series/${seriesId}?path=${encodedPath}`,
        method: 'GET',
        dataType: 'json'
      }).request;

      promise.done((data: FileEntry[]) => {
        setFiles(data);
        setLoading(false);
      });

      promise.fail((xhr: any) => {
        setError(xhr.statusText || 'Failed to load files');
        setLoading(false);
      });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load files');
      setLoading(false);
    }
  }, [seriesId]);

  useEffect(() => {
    loadFiles(currentPath);
  }, [currentPath, loadFiles]);

  const handleNavigateFolder = useCallback((path: string) => {
    setCurrentPath(path);
  }, []);

  const handleSelectFile = useCallback((filePath: string) => {
    onSelect(filePath);
  }, [onSelect]);

  return (
    <div className={styles.filePickerModal}>
      <div className={styles.header}>
        <h3>{translate('SelectFile')}</h3>
        <Button
          kind={kinds.DANGER}
          size="small"
          onPress={onClose}
        >
          ✕
        </Button>
      </div>

      <div className={styles.pathBar}>
        <span className={styles.currentPath}>{currentPath}</span>
      </div>

      {error && (
        <div className={styles.error}>{error}</div>
      )}

      {loading ? (
        <div className={styles.loading}>{translate('Loading')}...</div>
      ) : (
        <div className={styles.fileList}>
          {files.length === 0 ? (
            <div className={styles.empty}>{translate('NoFiles')}</div>
          ) : (
            files.map((file, index) => (
              <div
                key={`${file.path}-${index}`}
                className={styles.fileEntry}
              >
                <Icon
                  name={file.isDirectory ? icons.FOLDER : icons.FILE}
                  className={styles.icon}
                />
                <span
                  className={styles.name}
                  onClick={() => {
                    if (file.isDirectory || file.isParent) {
                      handleNavigateFolder(file.path);
                    }
                  }}
                  style={{ cursor: file.isDirectory || file.isParent ? 'pointer' : 'default' }}
                >
                  {file.name}
                </span>
                {!file.isDirectory && !file.isParent && (
                  <Button
                    kind={kinds.SUCCESS}
                    size="small"
                    onPress={() => handleSelectFile(file.path)}
                  >
                    {translate('Select')}
                  </Button>
                )}
              </div>
            ))
          )}
        </div>
      )}

      <div className={styles.footer}>
        <Button
          kind={kinds.DANGER}
          onPress={onClose}
        >
          {translate('Cancel')}
        </Button>
      </div>
    </div>
  );
}

export default FilePickerModal;
