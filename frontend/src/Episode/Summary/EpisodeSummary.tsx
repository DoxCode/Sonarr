import React, { useCallback, useEffect, useState } from 'react';
import { useDispatch } from 'react-redux';
import Icon from 'Components/Icon';
import Label from 'Components/Label';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import Button from 'Components/Link/Button';
import TextInput from 'Components/Form/TextInput';
import FilePickerModal from 'Components/Modal/FilePickerModal';
import Episode from 'Episode/Episode';
import useEpisode, { EpisodeEntities } from 'Episode/useEpisode';
import useEpisodeFile from 'EpisodeFile/useEpisodeFile';
import { icons, kinds, sizes } from 'Helpers/Props';
import Series from 'Series/Series';
import useSeries from 'Series/useSeries';
import QualityProfileNameConnector from 'Settings/Profiles/Quality/QualityProfileNameConnector';
import {
  deleteEpisodeFile,
  fetchEpisodeFile,
  moveEpisodeFile,
  associateEpisodeFile,
  unassociateEpisodeFile,
} from 'Store/Actions/episodeFileActions';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';
import EpisodeAiring from './EpisodeAiring';
import EpisodeFileRow from './EpisodeFileRow';
import styles from './EpisodeSummary.css';

const COLUMNS: Column[] = [
  {
    name: 'path',
    label: () => translate('Path'),
    isSortable: false,
    isVisible: true,
  },
  {
    name: 'size',
    label: () => translate('Size'),
    isSortable: false,
    isVisible: true,
  },
  {
    name: 'languages',
    label: () => translate('Languages'),
    isSortable: false,
    isVisible: true,
  },
  {
    name: 'quality',
    label: () => translate('Quality'),
    isSortable: false,
    isVisible: true,
  },
  {
    name: 'customFormats',
    label: () => translate('Formats'),
    isSortable: false,
    isVisible: true,
  },
  {
    name: 'customFormatScore',
    label: React.createElement(Icon, {
      name: icons.SCORE,
      title: () => translate('CustomFormatScore'),
    }),
    isSortable: true,
    isVisible: true,
  },
  {
    name: 'actions',
    label: '',
    isSortable: false,
    isVisible: true,
  },
];

interface EpisodeSummaryProps {
  seriesId: number;
  episodeId: number;
  episodeEntity: EpisodeEntities;
  episodeFileId?: number;
}

function EpisodeSummary(props: EpisodeSummaryProps) {
  const { seriesId, episodeId, episodeEntity, episodeFileId } = props;

  const dispatch = useDispatch();
  const [editingPath, setEditingPath] = useState(false);
  const [newPath, setNewPath] = useState('');
  const [isSaving, setIsSaving] = useState(false);
  const [selectedFilePath, setSelectedFilePath] = useState('');
  const [showFilePicker, setShowFilePicker] = useState(false);
  const [justUnassociated, setJustUnassociated] = useState(false);

  const { qualityProfileId, network } = useSeries(seriesId) as Series;

  const { path: seriesPath } = useSeries(seriesId) as Series;

  const episode = useEpisode(
    episodeId,
    episodeEntity
  ) as Episode;

  const { airDateUtc, overview } = episode;

  // Use episodeFileId from props, but also get from Redux to ensure we have latest
  // This ensures when unassociate happens, we update immediately
  const effectiveEpisodeFileId = episode?.episodeFileId || episodeFileId;

  const {
    path,
    mediaInfo,
    size,
    languages,
    quality,
    qualityCutoffNotMet,
    customFormats,
    customFormatScore,
  } = useEpisodeFile(effectiveEpisodeFileId) || {};

  const handleDeleteEpisodeFile = useCallback(() => {
    dispatch(
      deleteEpisodeFile({
        id: effectiveEpisodeFileId,
        episodeEntity,
      })
    );
  }, [effectiveEpisodeFileId, episodeEntity, dispatch]);

  const handleEditPath = useCallback(() => {
    setNewPath(path || '');
    setEditingPath(true);
  }, [path]);

  const handleCancelEdit = useCallback(() => {
    setEditingPath(false);
    setNewPath('');
  }, []);

  const handleSavePath = useCallback(() => {
    if (newPath && newPath !== path && effectiveEpisodeFileId) {
      setIsSaving(true);
      dispatch(
        moveEpisodeFile({
          id: effectiveEpisodeFileId,
          newPath,
        })
      );
      // Reset form after a brief delay to allow Redux action to process
      setTimeout(() => {
        setEditingPath(false);
        setNewPath('');
        setIsSaving(false);
        // Refresh episode file data to get any updates from parsing
        dispatch(fetchEpisodeFile({ id: effectiveEpisodeFileId }));
      }, 500);
    }
  }, [newPath, path, effectiveEpisodeFileId, dispatch]);

  const handlePathChange = useCallback((change: InputChanged<unknown>) => {
    setNewPath(String(change.value));
  }, []);

  const handleSelectFile = useCallback(() => {
    setShowFilePicker(true);
  }, []);

  const handleFilePickerSelect = useCallback((filePath: string) => {
    setSelectedFilePath(filePath);
    setShowFilePicker(false);
  }, []);

  const handleFilePickerClose = useCallback(() => {
    setShowFilePicker(false);
  }, []);

  const handleAssociateFile = useCallback(() => {
    if (selectedFilePath && episodeId) {
      setIsSaving(true);
      dispatch(
        associateEpisodeFile({
          episodeId,
          filePath: selectedFilePath,
        })
      );
      // Reset form after a brief delay to allow Redux action to process
      // and refetch the episode to get the updated episodeFileId
      setTimeout(() => {
        setSelectedFilePath('');
        setIsSaving(false);
        // Import and dispatch fetch episode to get updated episodeFileId
        // This will automatically re-render the component with the new file info
      }, 500);
    }
  }, [selectedFilePath, episodeId, dispatch]);

  const handleCancelAssociate = useCallback(() => {
    setSelectedFilePath('');
  }, []);

  const handleUnassociateFile = useCallback(() => {
    if (episodeId && effectiveEpisodeFileId) {
      setJustUnassociated(true);
      setIsSaving(true);
      setEditingPath(false);
      setNewPath('');
      dispatch(
        unassociateEpisodeFile({
          episodeId,
          episodeFileId: effectiveEpisodeFileId,
        })
      );
      // Reset form after a brief delay to allow Redux action to process
      setTimeout(() => {
        setIsSaving(false);
        setJustUnassociated(false);
        // Component will re-render with episodeFileId = 0 from Redux
        // useEpisodeFile(0) will return undefined
        // and the component will show the Associate section
      }, 500);
    }
  }, [episodeId, effectiveEpisodeFileId, dispatch]);

  useEffect(() => {
    // Only fetch episode file if we have a valid ID and path hasn't loaded yet
    // This prevents trying to fetch a file that was just deleted (unassociated)
    // Also skip if we just unassociated to prevent loading old file ID
    if (effectiveEpisodeFileId && effectiveEpisodeFileId > 0 && !path && !justUnassociated) {
      dispatch(fetchEpisodeFile({ id: effectiveEpisodeFileId }));
    }
  }, [effectiveEpisodeFileId, path, justUnassociated, dispatch]);

  const hasOverview = !!overview;

  return (
    <div>
      <div>
        <span className={styles.infoTitle}>{translate('Airs')}</span>

        <EpisodeAiring airDateUtc={airDateUtc} network={network} />
      </div>

      <div>
        <span className={styles.infoTitle}>{translate('QualityProfile')}</span>

        <Label kind={kinds.PRIMARY} size={sizes.MEDIUM}>
          <QualityProfileNameConnector qualityProfileId={qualityProfileId} />
        </Label>
      </div>

      <div className={styles.overview}>
        {hasOverview ? overview : translate('NoEpisodeOverview')}
      </div>

      {path ? (
        <>
          <Table columns={COLUMNS}>
            <TableBody>
              <EpisodeFileRow
                path={path}
                size={size!}
                languages={languages!}
                quality={quality!}
                qualityCutoffNotMet={qualityCutoffNotMet!}
                customFormats={customFormats!}
                customFormatScore={customFormatScore!}
                mediaInfo={mediaInfo!}
                columns={COLUMNS}
                onDeleteEpisodeFile={handleDeleteEpisodeFile}
              />
            </TableBody>
          </Table>

          <div className={styles.filePathEditor}>
            <span className={styles.infoTitle}>{translate('EditFilePath')}</span>
            <div className={styles.pathInputContainer}>
              <TextInput
                value={editingPath ? newPath : path}
                onChange={handlePathChange}
                readOnly={!editingPath}
                className={styles.pathInput}
                name="episodeFilePath"
              />
              <div className={styles.buttonGroup}>
                {!editingPath ? (
                  <>
                    <Button
                      kind={kinds.PRIMARY}
                      onPress={handleEditPath}
                      isDisabled={isSaving}
                    >
                      {translate('Edit')}
                    </Button>
                    <Button
                      kind={kinds.DANGER}
                      onPress={handleUnassociateFile}
                      isDisabled={isSaving}
                    >
                      {translate('Unassociate')}
                    </Button>
                  </>
                ) : (
                  <>
                    <Button
                      kind={kinds.SUCCESS}
                      onPress={handleSavePath}
                      isDisabled={isSaving || newPath === path}
                    >
                      {translate('Save')}
                    </Button>
                    <Button
                      kind={kinds.DANGER}
                      onPress={handleCancelEdit}
                      isDisabled={isSaving}
                    >
                      {translate('Cancel')}
                    </Button>
                  </>
                )}
              </div>
            </div>
          </div>
        </>
      ) : (
        <div className={styles.filePathEditor}>
          <span className={styles.infoTitle}>{translate('AssociateFile')}</span>
          <div className={styles.pathInputContainer}>
            <TextInput
              value={selectedFilePath}
              onChange={(change: InputChanged<unknown>) => setSelectedFilePath(String(change.value))}
              placeholder={`${seriesPath || translate('SeriesPath')}/...`}
              className={styles.pathInput}
              name="episodeFilePathAssociate"
            />
            <div className={styles.buttonGroup}>
              <Button
                kind={kinds.PRIMARY}
                onPress={handleSelectFile}
                isDisabled={isSaving}
              >
                {translate('Browse')}
              </Button>
              <Button
                kind={kinds.SUCCESS}
                onPress={handleAssociateFile}
                isDisabled={isSaving || !selectedFilePath}
              >
                {translate('Associate')}
              </Button>
              {selectedFilePath && (
                <Button
                  kind={kinds.DANGER}
                  onPress={handleCancelAssociate}
                  isDisabled={isSaving}
                >
                  {translate('Clear')}
                </Button>
              )}
            </div>
          </div>
        </div>
      )}

      {showFilePicker && seriesPath && (
        <FilePickerModal
          seriesId={seriesId}
          seriesPath={seriesPath}
          onSelect={handleFilePickerSelect}
          onClose={handleFilePickerClose}
        />
      )}
    </div>
  );
}

export default EpisodeSummary;
